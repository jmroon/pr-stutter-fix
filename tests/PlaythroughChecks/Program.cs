using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using PRStutter.PlaythroughDiagnostics;

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
var ring = new RecentBuffer<int>(4);
for (int i = 0; i < 12; i++) ring.Add(i, i);
Check(ring.Overwritten == 8 && ring.Snapshot(0).SequenceEqual(new[] {8,9,10,11}), "Rolling buffer order/capacity wrong");
Check(ring.Snapshot(10).SequenceEqual(new[] {10,11}) && ring.Snapshot(12).Length == 0,"Time-window filtering wrong");
var snapshot = ring.Snapshot(0); ring.Add(12,12);
Check(snapshot[0] == 8, "Save snapshot shares mutable ring storage");
var gate = new IncidentGate(30,3);
Check(gate.Trigger("unsupported scene",100) && gate.DueQpc == 103, "Post-event capture missing");
Check(!gate.Trigger("marker",101,true), "Concurrent incident admitted");
gate.Complete();
Check(!gate.Trigger("new scene",110) && !gate.Trigger("unsupported scene",150), "Cooldown/dedup bypassed");
Check(gate.Trigger("marker",111,true), "Manual marker must bypass automatic cooldown");
gate.Complete(); Check(gate.Trigger("new scene",151), "New scene did not become eligible");
gate.Complete();
for (int i=0; i<200; i++) { gate.Trigger("scene"+i,200+i*31); gate.Complete(); }
Check(!gate.Trigger("overflow",10000), "Unbounded incident keys");
string directory = Path.Combine(Path.GetTempPath(), "prstutter-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try {
    var writer = new CaptureWriter(directory, 1024, 2);
    Check(writer.TrySave(new { Evidence = "one", Values = snapshot }), "Write rejected"); writer.Wait();
    var file = Directory.GetFiles(directory, "*.json").Single();
    using (var json = JsonDocument.Parse(File.ReadAllText(file))) Check(json.RootElement.GetProperty("Evidence").GetString()=="one","Capture not serialized");
    Check(!Directory.GetFiles(directory,"*.partial").Any(),"Completed capture left partial file");
    writer.TrySave(new { Evidence="two" }); writer.Wait();
    writer.TrySave(new { Evidence="three" }); writer.Wait();
    Check(Directory.GetFiles(directory).Length==2 && writer.Status.Contains("budget"),"File quota failed");
    string blocked = Path.Combine(directory,"not-a-directory"); File.WriteAllText(blocked,"keep");
    var failing = new CaptureWriter(blocked); failing.TrySave(new { Evidence="fail" }); failing.Wait();
    Check(failing.Status.StartsWith("save failed") && File.ReadAllText(blocked)=="keep","Write error escaped or overwrote unrelated file");
} finally { Directory.Delete(directory,true); }
Console.WriteLine("PASS: bounded chronological rings, independent snapshots, incident dedup/cooldown/post-window, manual markers, disk quotas, atomic completed files and write failure isolation.");

using var stream = File.OpenRead(args[0]); using var pe = new PEReader(stream); var reader = pe.GetMetadataReader();
foreach (var handle in reader.AssemblyReferences) {
    string name = reader.GetString(reader.GetAssemblyReference(handle).Name);
    Check(!name.Contains("Harmony") && !name.Contains("MonoMod"), "Diagnostic plugin references native patching");
}
foreach (var h in reader.MethodDefinitions) Check((reader.GetMethodDefinition(h).Attributes & MethodAttributes.PinvokeImpl)==0,"Diagnostic native import");
foreach (var h in reader.MemberReferences) {
    var m=reader.GetMemberReference(h); if(m.Parent.Kind!=HandleKind.TypeReference)continue;
    var t=reader.GetTypeReference((TypeReferenceHandle)m.Parent); string ns=reader.GetString(t.Namespace), type=reader.GetString(t.Name), name=reader.GetString(m.Name);
    if(ns.StartsWith("UnityEngine")||ns.StartsWith("Last.")) {
        Check(!name.StartsWith("set_"),"Diagnostic writes game state: "+type+"."+name);
        Check(name is not ("ReadPixels" or "Blit" or "MoveTo" or "UpdateEntity" or "UpdateController" or "SetTexture"),"Intrusive diagnostic call: "+name);
    }
}
Console.WriteLine("PASS: diagnostics assembly has no Harmony/MonoMod, native imports, game/Unity setters, GPU readback or correction execution calls.");
