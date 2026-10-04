"""Read the installed compositor shader; write analysis only beneath artifacts/shaders.

Run with .tools/asset-python/Scripts/python.exe after Setup-AssetReader.ps1.
Uses UnityPy's shader parser and Windows D3DDisassemble. Never saves game assets.
"""
import argparse
import ctypes as c
import hashlib
import json
from pathlib import Path

import UnityPy
from UnityPy.export.ShaderConverter import ShaderProgram
from UnityPy.helpers import CompressionHelper
from UnityPy.streams import EndianBinaryReader


def disassemble(data):
    dll = c.WinDLL(r"C:\Windows\System32\d3dcompiler_47.dll")
    function = dll.D3DDisassemble
    function.argtypes = [c.c_void_p, c.c_size_t, c.c_uint, c.c_char_p, c.POINTER(c.c_void_p)]
    function.restype = c.c_long
    buffer, result = c.create_string_buffer(data), c.c_void_p()
    status = function(buffer, len(data), 0, None, c.byref(result))
    if status < 0:
        raise RuntimeError(f"D3DDisassemble failed: {status & 0xffffffff:08X}")
    vtable = c.cast(result, c.POINTER(c.POINTER(c.c_void_p))).contents
    pointer = c.WINFUNCTYPE(c.c_void_p, c.c_void_p)(vtable[3])
    size = c.WINFUNCTYPE(c.c_size_t, c.c_void_p)(vtable[4])
    release = c.WINFUNCTYPE(c.c_ulong, c.c_void_p)(vtable[2])
    try:
        return c.string_at(pointer(result), size(result)).rstrip(b"\0")
    finally:
        release(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-directory", type=Path,
                        default=Path(r"D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR"))
    parser.add_argument("--shader", default="Last/PostProcessLite")
    parser.add_argument("--prefix", default="compositor")
    parser.add_argument("--source", type=Path)
    parser.add_argument("--pass-index", type=int, default=0)
    args = parser.parse_args()
    source = args.source or args.game_directory / "FINAL FANTASY VI_Data/sharedassets0.assets"
    output = Path(__file__).resolve().parents[1] / "artifacts/shaders"
    output.mkdir(parents=True, exist_ok=True)
    env = UnityPy.load(str(source))
    matches = [obj for obj in env.objects if obj.type.name == "Shader"
               and obj.read().m_ParsedForm.m_Name == args.shader]
    if len(matches) != 1:
        raise RuntimeError(f"Expected one {args.shader} shader, got {len(matches)}")
    obj = matches[0]
    shader = obj.read()
    (output / f"{args.prefix}.json").write_text(json.dumps(obj.read_typetree(), indent=2), encoding="utf-8")
    (output / f"{args.prefix}.shader.txt").write_text(shader.export(), encoding="utf-8")
    def first(values):
        return values[0][0] if isinstance(values[0], list) else values[0]
    offset, length = first(shader.offsets), first(shader.compressedLengths)
    blob = CompressionHelper.decompress_lz4(bytes(shader.compressedBlob)[offset:offset + length],
                                          first(shader.decompressedLengths))
    program = ShaderProgram(EndianBinaryReader(blob, endian="<"), shader.object_reader.version)
    exported = []
    # Use stage metadata, not assumed positions in the subprogram blob.
    for stage in ("progVertex", "progFragment"):
        variants = obj.read_typetree()["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"][args.pass_index][stage]["m_SubPrograms"]
        for variant in variants:
            index = variant["m_BlobIndex"]
            subprogram = program.m_SubPrograms[index]
            code = subprogram.m_ProgramCode
            start = code.find(b"DXBC")
            if start < 0 or start + 28 > len(code):
                raise ValueError("Direct3D program header missing")
            size = int.from_bytes(code[start + 24:start + 28], "little")
            if size < 32 or start + size > len(code):
                raise ValueError("Invalid DXBC container length")
            dxbc = code[start:start + size]
            name = f"{args.prefix}-{stage}-{index}"
            (output / (name + ".dxbc")).write_bytes(dxbc)
            (output / (name + ".asm")).write_bytes(disassemble(dxbc))
            exported.append(dict(Name=name, Keywords=subprogram.m_Keywords))
    manifest = dict(Source=str(source), Sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                    ShaderPathId=obj.path_id, PassIndex=args.pass_index, UnityPy=UnityPy.__version__, Programs=exported)
    (output / f"{args.prefix}-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
