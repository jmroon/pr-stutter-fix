using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using PRStutter.GridExperiment;
using PRStutter.RenderExperiment;
using PRStutter.UnroundedExperiment;

internal static class ResolutionChecks
{
    static void Check(bool ok,string reason) { if(!ok)throw new Exception(reason); }
    static void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Invalid render sequence accepted"); }
    public static void Run(string assembly)
    {
        foreach(int count in new[]{2,4}) {
            var frame=new ResolutionFrame(count);
            frame.Before(0,1,false); Check(!frame.After(0,1,false),"Unprepared startup counted");
            frame.Prepared(1); Throws(()=>frame.Before(1,99,true));
            for(int i=0;i<count;i++) { frame.Before(1,i,false); Check(!frame.After(1,i,false),"Field camera completed whole frame"); }
            Throws(()=>frame.After(1,99,true)); // Final pre-cull must have run.
            frame.Before(1,99,true); Throws(()=>frame.Before(1,99,true));
            Check(frame.After(1,99,true)&&frame.CompletedFrames==1,"Complete render not counted");
            frame.Restored(); Throws(()=>frame.Before(1,1,false));
            frame.Prepared(2); frame.Before(2,1,false); Throws(()=>frame.Before(2,1,false));
            Throws(()=>frame.After(2,2,false)); frame.After(2,1,false); Throws(()=>frame.After(2,1,false));
            Throws(()=>frame.Before(3,3,false)); // Missing next-frame preparation.
            frame.Restored(); frame.Prepared(4);
            for(int i=0;i<count;i++) { frame.Before(4,i,false); frame.After(4,i,false); }
            frame.Before(4,99,true); Check(frame.After(4,99,true)&&frame.CompletedFrames==2,"Frame restart failed");
        }
        Check(FieldLayoutPolicy.Accepts("FFIV",new[]{"CameraFieldMain","CameraTileMap"},1),"IV layout");
        Check(!FieldLayoutPolicy.Accepts("FFVI",new[]{"CameraFieldMain","CameraTileMap"},1),"Cross-game layout accepted");
        // Real ownership helper: restore after setter writes then throws; preserve
        // native changes; retain ownership on cleanup failure before any release.
        string current="stock"; bool applyFailure=true, restoreFailure=false;
        var binding=new ScopedOverride<string>(()=>current,value=>{
            if(value=="stock"&&restoreFailure)throw new IOException("restore");
            current=value;
            if(value=="high"&&applyFailure)throw new IOException("apply");
        });
        Throws(()=>binding.Apply("high")); Check(binding.Pending,"Lost ownership after partial apply");
        binding.Restore(); Check(current=="stock"&&!binding.Pending,"Partial apply cleanup");
        applyFailure=false; binding.Apply("high"); restoreFailure=true;
        Throws(binding.Restore); Check(binding.Pending,"Lost reference on cleanup failure");
        restoreFailure=false; binding.Restore();
        binding.Apply("high"); current="new-native-target"; binding.Restore();
        Check(current=="new-native-target"&&!binding.Pending,"Overwrote native replacement");

        using var stream=File.OpenRead(assembly);using var pe=new PEReader(stream);var reader=pe.GetMetadataReader();
        foreach(var h in reader.AssemblyReferences) {
            string name=reader.GetString(reader.GetAssemblyReference(h).Name);
            Check(!name.StartsWith("PRStutter."),"Runtime dependency on older compensation module");
        }
        foreach(var h in reader.MemberReferences) {
            var m=reader.GetMemberReference(h); if(m.Parent.Kind!=HandleKind.TypeReference)continue;
            var t=reader.GetTypeReference((TypeReferenceHandle)m.Parent);
            string ns=reader.GetString(t.Namespace),type=reader.GetString(t.Name),name=reader.GetString(m.Name);
            Check(type is not ("VisualMotion" or "MotionResidual" or "StabilizationMath"),"Position compensation linked");
            if(ns.StartsWith("UnityEngine")) {
                Check(!(type=="Transform"&&name.StartsWith("set_")),"Transform mutation: "+name);
                Check(!(type=="Camera"&&name.StartsWith("set_")&&name!="set_targetTexture"),"Camera pose/projection mutation: "+name);
                Check(name is not ("set_targetFrameRate" or "set_vSyncCount" or "set_timeScale" or "SetFloat" or "SetVector" or "SetMatrix" or "ReadPixels"),"Out-of-scope rendering/pacing mutation: "+name);
            }
            if(ns.StartsWith("Last.")) Check(!name.StartsWith("set_") && name is not ("MoveTo" or "UpdateEntity" or "UpdateController"),"Game method mutation: "+name);
        }
        Console.WriteLine("PASS: complete draw order, startup/recovery, ownership cleanup, topology; compiled plugin has no pose/projection/pacing writes or old compensation dependency.");
    }
}
