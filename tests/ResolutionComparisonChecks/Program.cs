using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using PRStutter.UnroundedExperiment;

static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
Check(ResolutionScale.Width(4)==1280 && ResolutionScale.Height(4)==720 && ResolutionScale.Width(8)==2560 && ResolutionScale.Height(8)==1440,"Render size mismatch");
Check(ResolutionScale.Next(1)==4 && ResolutionScale.Next(4)==8 && ResolutionScale.Next(8)==1,"Comparison cycle mismatch");
foreach (int invalid in new[] {0,1,2,16}) {
    bool refused=false;
    try { ResolutionScale.Width(invalid); } catch (ArgumentOutOfRangeException) { refused=true; }
    Check(refused,"Unsupported allocation scale accepted");
}
var gate = new MovementGate(true,true,true,true,false,"field-manual","map1",1,100,200,10);
Check(gate.Eligible(11),"Fresh manual context refused");
foreach (var rejected in new[] { gate with { Enabled=false }, gate with { Precision=false }, gate with { Timing=false },
    gate with { Pacing=false }, gate with { Faulted=true }, gate with { Kind="field-event" }, gate with { Field=0 },
    gate with { Map=0 }, gate with { ContextFrame=8 }, gate with { ContextFrame=12 } })
    Check(!rejected.Eligible(11),"Ineligible comparison accepted");
Check(!gate.SameContext(gate with { Generation=2 }) && !gate.SameContext(gate with { Map=201 }) &&
    !gate.SameContext(gate with { Field=101 }) && !gate.SameContext(gate with { Identity="map2" }),"Context replacement accepted");
var readiness = new ResolutionReadiness<string>();
Check(!readiness.ShouldStart("a",true,0,1) && !readiness.ShouldStart("a",true,.1,2),"Started before stable interval");
Check(!readiness.ShouldStart("a",true,.3,1),"Same-frame observation armed rendering");
Check(readiness.ShouldStart("a",true,.3,3),"Stable eligible scene did not start");
for(int i=0;i<1000;i++) Check(!readiness.ShouldStart("a",true,i+1,i+4),"Failed start retried without context change");
Check(!readiness.ShouldStart("b",true,2000,2000) && readiness.ShouldStart("b",true,2000.3,2001),"New context did not rearm");
Check(!readiness.ShouldStart("b",false,2001,2002),"Unsupported scene started");
Check(!readiness.ShouldStart("b",true,2002,2003) && readiness.ShouldStart("b",true,2002.3,2004),"Scene/focus/CRT return did not rearm");
Check(!readiness.ShouldStart("b:revision2",true,2003,2005) && readiness.ShouldStart("b:revision2",true,2003.3,2006),"Explicit retry did not rearm");
Console.WriteLine("PASS: stable eligibility, one attempt per context, retry and scene/focus/effect reacquisition.");
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
