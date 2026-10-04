using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using PRStutter.PresentationAudit;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Near(float actual, float expected, string reason) => Check(Math.Abs(actual - expected) < .0001f, reason);
Near(AuditModel.Axis(150, 400, 200, false, true, 7),107,"Offset/clamp ordering");
Near(AuditModel.Axis(-150, 400, 200, false, true, 7),-93,"Negative boundary");
Near(AuditModel.Axis(150, 400, 200, true, true, 7),157,"Loop incorrectly clamped");
Near(AuditModel.Axis(150, 400, 200, false, false, 7),157,"Disabled clamp ignored");
Near(AuditModel.Axis(150, 100, 200, false, true, 7),7,"Small map extent");
var camera = AuditModel.Camera(new(70,-95),new(5,-5),new(3,4),200,200,100,100,false,false,true);
Near(camera.X,53,"Camera X"); Near(camera.Y,-46,"Camera Y");
Near(AuditModel.WrappedVisualAxis(-470,480,480,1000,200,true),50,"Positive loop wrap");
Near(AuditModel.WrappedVisualAxis(470,-480,-480,1000,200,true),-50,"Negative loop wrap");
Near(AuditModel.WrappedVisualAxis(-470,480,480,1000,200,false),-950,"No-loop changed");
Near(AuditModel.WrappedVisualAxis(-470,480,480,1000,600,true),-950,"Small looping map changed");
var visual = AuditModel.Visual(new(30,40),new(10,15),new(),1000,1000,200,100,false,false,new(2,.5f));
Near(visual.X,40,"Layer scale X"); Near(visual.Y,12.5f,"Layer scale Y");
Check(AuditModel.Compare(new(1,2),new(1,2,99)).Status=="match","Depth must be excluded from XY check");
Check(AuditModel.Compare(new(1,2),new(1.1f,2)).Status=="mismatch","Mismatch hidden");
Check(AuditModel.Compare(new(1,2),new(1,2),"corrections-enabled").Status=="corrections-enabled","Contaminated match accepted");
Check(AuditModel.Compare(new(float.NaN,2),new()).Status=="nonfinite","NaN accepted");
var window = new AuditWindow(1000); window.Start(0);
Check(window.Take(0)&&!window.Take(49)&&window.Take(50),"20Hz gate");
Check(window.Take(1000)&&!window.Take(1000)&&!window.Take(1049),"Hitch catch-up burst");
Check(!window.Take(60000)&&window.Expired(60000),"Duration bound");
window.Stop();Check(!window.Take(70000)&&!window.Expired(70000),"Stopped observer active");
window.Start(70000);Check(window.Take(70000),"Restart failed");
Console.WriteLine("PASS: native clamp/offset ordering, small and looping maps, independent actor projection, layer scale, nonfinite/contamination refusal and bounded sampling.");

using var stream=File.OpenRead(args[0]);using var pe=new PEReader(stream);var reader=pe.GetMetadataReader();
foreach(var h in reader.AssemblyReferences) {
    string name=reader.GetString(reader.GetAssemblyReference(h).Name);
    Check(!name.StartsWith("PRStutter."),"Audit depends on a correction/debug assembly: "+name);
}
foreach(var h in reader.MethodDefinitions) Check((reader.GetMethodDefinition(h).Attributes&MethodAttributes.PinvokeImpl)==0,"Native import");
int patches=0, callbacks=0;
foreach(var h in reader.MethodDefinitions) {
    var m=reader.GetMethodDefinition(h);
    if(reader.GetString(m.Name)!="AfterVisuals")continue;
    callbacks++;
    var sig=reader.GetBlobReader(m.Signature);sig.ReadSignatureHeader();
    Check(sig.ReadCompressedInteger()==1&&sig.ReadSignatureTypeCode()==SignatureTypeCode.Void,"Observer must be a one-argument void postfix");
    Check(sig.ReadSignatureTypeCode()==SignatureTypeCode.TypeHandle,"Writable/byref observer argument");
}
foreach(var h in reader.MemberReferences) {
    var m=reader.GetMemberReference(h);if(m.Parent.Kind!=HandleKind.TypeReference)continue;
    var t=reader.GetTypeReference((TypeReferenceHandle)m.Parent);
    string ns=reader.GetString(t.Namespace),type=reader.GetString(t.Name),name=reader.GetString(m.Name);
    if(ns.StartsWith("UnityEngine")||ns.StartsWith("Last.")) {
        Check(!name.StartsWith("set_"),"Game setter: "+type+"."+name);
        Check(name is not ("MoveTo" or "UpdateEntity" or "UpdateController" or "UpdateVisualInstancePosition" or "UpdateMapScrollIfNeed" or "SetTexture" or "ReadPixels" or "Blit" or "FindObjectsOfType"),"Intrusive/scene-scanning call: "+name);
    }
    if(ns=="HarmonyLib") { if(name=="Patch")patches++; Check(name!="PatchAll","Unbounded patch registration"); }
    if(ns=="System.Reflection")Check(name!="SetValue","Reflective mutation");
}
Check(patches==1&&callbacks==1,"Expected one observational patch boundary");
Console.WriteLine("PASS: independent assembly; exactly one void observation callback; no game/Unity setters, correction execution, scene scans, reflective writes or native imports.");
