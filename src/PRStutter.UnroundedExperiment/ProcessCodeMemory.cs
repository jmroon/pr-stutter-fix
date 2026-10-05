using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PRStutter.UnroundedExperiment;

internal sealed class ProcessCodeMemory : ICodeMemory
{
    private readonly IntPtr _base;
    private readonly int _size;
    public ProcessCodeMemory(IntPtr address, int size) { _base = address; _size = size; }
    private IntPtr Address(int rva, int length)
    {
        if (rva < 0 || length <= 0 || (long)rva + length > _size) throw new ArgumentOutOfRangeException(nameof(rva));
        return IntPtr.Add(_base, rva);
    }
    public byte[] Read(int rva, int length)
    {
        var bytes = new byte[length]; Marshal.Copy(Address(rva, length), bytes, 0, length); return bytes;
    }
    public void Write(int rva, byte[] bytes)
    {
        var address = Address(rva, bytes.Length);
        if (!VirtualProtect(address, (UIntPtr)bytes.Length, 0x40, out uint old)) throw new Win32Exception();
        try {
            Marshal.Copy(bytes, 0, address, bytes.Length);
            if (!FlushInstructionCache(GetCurrentProcess(), address, (UIntPtr)bytes.Length)) throw new Win32Exception();
        } finally {
            if (!VirtualProtect(address, (UIntPtr)bytes.Length, old, out _)) throw new Win32Exception();
        }
    }
    [DllImport("kernel32", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protection, out uint previous);
    [DllImport("kernel32", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
    [DllImport("kernel32")] private static extern IntPtr GetCurrentProcess();
}
