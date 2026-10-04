using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using PRStutter.TimingExperiment;
using UnityEngine;

namespace PRStutter.PlaythroughDiagnostics;

[BepInPlugin("local.prstutter.playthrough", "PR Stutter Playthrough Diagnostics", "0.1.1")]
[BepInDependency("local.prstutter.timing", "0.5.1")]
public sealed class Plugin : BasePlugin
{
    private Driver? _driver;
    public override void Load()
    {
        if (!CorrectionStatus.Available) { Log.LogWarning("Supported correction runtime unavailable; diagnostics disabled."); return; }
        Playthrough.Initialize(Config);
        _driver = AddComponent<Driver>();
        Log.LogInfo("Optional playthrough diagnostics ready. F10 toggles recording; F11 marks visible jitter. No GPU readback or native hooks. F8 remains the old standalone capture.");
    }
    public override bool Unload()
    {
        Playthrough.Shutdown();
        if (_driver != null) UnityEngine.Object.Destroy(_driver);
        return true;
    }
}
public sealed class Driver : MonoBehaviour
{
    public Driver(IntPtr pointer) : base(pointer) { }
    public void Update() { try { Playthrough.Keys(); } catch (Exception e) { Playthrough.Fault(e); } }
    public void LateUpdate() { try { Playthrough.Sample(); } catch (Exception e) { Playthrough.Fault(e); } }
    public void OnGUI() { try { GUI.Label(new Rect(12, Screen.height - 40, Math.Min(1100, Screen.width - 24), 30), Playthrough.Status); } catch { } }
    public void OnApplicationQuit() => Playthrough.Shutdown();
}
