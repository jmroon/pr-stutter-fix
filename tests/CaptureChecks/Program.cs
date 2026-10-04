using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using PRStutter.Diagnostics;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

string output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
string stem = Path.Combine(output, "capture-check-" + Guid.NewGuid().ToString("N"));
var buffer = new CaptureBuffer(2);
var sample = new CaptureSample {
    Phase = Phase.MovementResult, Frame = 1, EntityId = 42, ControllerId = 7, CameraId = 3,
    PlayerMoveState = 0, Qpc = 9007199254740993, ObserverTicks = 25,
    DeltaTime = 1f / 60, Timer = 0.125f, Duration = 0.24f,
    Candidate = new Point3(-1.5f, 12.25f, 0), Result = new Point3(-2f, 12f, 0),
    SpriteWorld = new Point3(2.5f, 3.75f, -4f), SpriteScreenPoint = new Point3(100.5f, 200.75f, 1f),
    CameraWorld = Point3.Missing, ScreenWidth = 1920, ScreenHeight = 1080
};
Check(buffer.Add(sample), "First record rejected");
sample.Frame = 2;
Check(buffer.Add(sample), "Second record rejected");
sample.Frame = 3;
Check(!buffer.Add(sample), "Buffer must stop accepting records at capacity");
Check(buffer.Count == 2 && buffer.Dropped == 1, "Capacity accounting failed");
Check(buffer.Samples[0].Frame == 1 && buffer.Samples[1].Frame == 2, "Earlier samples overwritten");
try { _ = new CaptureBuffer(0); throw new Exception("Zero capacity accepted"); }
catch (ArgumentOutOfRangeException) { }
var metadata = new CaptureMetadata(1, "test", "test", "test", DateTime.UtcNow.ToString("o"),
    10000000, 0, 100, "capacity", 2, 1, 2, 0, 0,
    new[] { new CameraDescription(3, "Camera, \"quoted\"", -1, "RT", 0) });
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-CA");
CaptureFiles.Write(stem, buffer, metadata);
string[] lines = File.ReadAllLines(stem + ".csv");
string[] header = lines[0].Split(',');
string[] values = lines[1].Split(',');
Check(lines.Length == 3, "CSV row count mismatch");
Check(header.Length == values.Length && header.Length == header.Distinct().Count(), "CSV schema mismatch");
string At(string name) => values[Array.IndexOf(header, name)];
Check(At("qpc") == "9007199254740993", "QPC lost integer precision");
Check(At("candidate_x") == "-1.5" && At("candidate_y") == "12.25", "Culture-dependent float formatting");
Check(At("result_x") == "-2" && At("sprite_screen_y") == "200.75", "Coordinates misaligned with header");
Check(At("camera_world_x") == "NaN", "Missing data must not be reported as zero");
Check(!File.Exists(stem + ".csv.partial") && !File.Exists(stem + ".json.partial"), "Completed output still partial");
using var json = JsonDocument.Parse(File.ReadAllText(stem + ".json"));
Check(json.RootElement.GetProperty("Rows").GetInt32() == 2, "Metadata row count mismatch");
Check(json.RootElement.GetProperty("Cameras")[0].GetProperty("Name").GetString() == "Camera, \"quoted\"", "Metadata escaping failed");
string original = File.ReadAllText(stem + ".csv");
try { CaptureFiles.Write(stem, buffer, metadata); throw new Exception("Existing capture overwritten"); }
catch (IOException) { }
Check(File.ReadAllText(stem + ".csv") == original, "Existing capture was changed");
Console.WriteLine("PASS: bounded buffer, preserved samples, CSV schema/culture/precision, metadata, completion markers, overwrite protection.");

// Inspect the built plugin without loading Unity or executing any game code.
using var assemblyStream = File.OpenRead(args[1]);
using var pe = new PEReader(assemblyStream);
var reader = pe.GetMetadataReader();
foreach (var handle in reader.AssemblyReferences)
{
    string name = reader.GetString(reader.GetAssemblyReference(handle).Name);
    Check(!name.Contains("Harmony", StringComparison.OrdinalIgnoreCase) && !name.Contains("MonoMod", StringComparison.OrdinalIgnoreCase),
        "Polling plugin references a patching library: " + name);
}
foreach (var handle in reader.MethodDefinitions)
    Check((reader.GetMethodDefinition(handle).Attributes & MethodAttributes.PinvokeImpl) == 0,
        "Polling plugin contains a native import");
foreach (var handle in reader.MemberReferences)
{
    var member = reader.GetMemberReference(handle);
    string name = reader.GetString(member.Name);
    if (member.Parent.Kind != HandleKind.TypeReference) continue;
    var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
    string ns = reader.GetString(type.Namespace);
    string typeName = reader.GetString(type.Name);
    if (ns.StartsWith("UnityEngine", StringComparison.Ordinal) || ns.StartsWith("Last.", StringComparison.Ordinal) || typeName == "CameraFollowing")
    {
        Check(!name.StartsWith("set_", StringComparison.Ordinal), "Game/Unity setter referenced: " + typeName + "." + name);
        Check(name is not ("UpdateMovingSetPosition" or "UpdateController" or "MoveTo" or "SetPosition" or "SetOffsetPosition"),
            "Game movement method referenced: " + typeName + "." + name);
    }
}
Console.WriteLine("PASS: compiled plugin has no Harmony/MonoMod references, P/Invoke declarations, or direct game/Unity setter references. Live behavior remains unverified.");
