using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using PRStutter.UnroundedExperiment;

static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
var gate = new MovementGate(true,true,true,true,false,"field-manual","map1",1,100,200,10);
Check(gate.Eligible(11),"Fresh manual context refused");
foreach (var rejected in new[] { gate with { Enabled=false }, gate with { Precision=false }, gate with { Timing=false },
    gate with { Pacing=false }, gate with { Faulted=true }, gate with { Kind="field-event" }, gate with { Field=0 },
    gate with { Map=0 }, gate with { ContextFrame=8 }, gate with { ContextFrame=12 } })
    Check(!rejected.Eligible(11),"Ineligible comparison accepted");
Check(!gate.SameContext(gate with { Generation=2 }) && !gate.SameContext(gate with { Map=201 }) &&
    !gate.SameContext(gate with { Field=101 }) && !gate.SameContext(gate with { Identity="map2" }),"Context replacement accepted");
using var stream=File.OpenRead(args[0]);using var pe=new PEReader(stream);var reader=pe.GetMetadataReader();
foreach(var h in reader.AssemblyReferences) {
    string name=reader.GetString(reader.GetAssemblyReference(h).Name);
    Check(!name.StartsWith("PRStutter.") && name!="0Harmony","Correction or patching dependency: "+name);
}
foreach(var h in reader.MethodDefinitions) Check((reader.GetMethodDefinition(h).Attributes&MethodAttributes.PinvokeImpl)==0,"Native import");
bool targetWrite=false,textureWrite=false;
foreach(var h in reader.MemberReferences) {
    var m=reader.GetMemberReference(h);if(m.Parent.Kind!=HandleKind.TypeReference)continue;
    var t=reader.GetTypeReference((TypeReferenceHandle)m.Parent);
    string ns=reader.GetString(t.Namespace),type=reader.GetString(t.Name),name=reader.GetString(m.Name);
    if(ns.StartsWith("UnityEngine")) {
        Check(!(type=="Transform"&&name.StartsWith("set_")),"Transform mutation");
        Check(!(type=="Camera"&&name.StartsWith("set_")&&name!="set_targetTexture"),"Camera pose/projection mutation");
        Check(name is not ("set_targetFrameRate" or "set_vSyncCount" or "set_timeScale" or "SetFloat" or "SetVector" or "SetMatrix" or "ReadPixels" or "Blit"),"Non-resolution mutation: "+name);
        targetWrite |= type=="Camera"&&name=="set_targetTexture";
        textureWrite |= type=="Material"&&name=="SetTexture";
    }
    if(ns.StartsWith("Last.")) Check(!name.StartsWith("set_") && name is not ("MoveTo" or "UpdateEntity" or "UpdateController"),"Game mutation");
    if(ns=="System.Reflection")Check(name is not ("SetValue" or "Invoke"),"Reflective correction mutation");
}
Check(targetWrite&&textureWrite,"Missing resolution bindings");
Console.WriteLine("PASS: fresh manual movement and same-context gating; add-on permits texture bindings but rejects timing/pacing/pose/game writes, native patches and correction dependencies.");
