using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using PRStutter.TimingExperiment;
using UnityEngine;

namespace PRStutter.PlaythroughDiagnostics;

[BepInPlugin("local.prstutter.playthrough", "PR Stutter Playthrough Diagnostics", "0.2.2")]
[BepInDependency("local.prstutter.timing", "0.6.2")]
public sealed class Plugin : BasePlugin
{
#if PR_FFIV
    private const string ExpectedGame = "FFIV";
#else
    private const string ExpectedGame = "FFVI";
#endif
    private Driver? _driver;
    public override void Load()
    {
        if (!CorrectionStatus.Available || CorrectionStatus.Game != ExpectedGame) { Log.LogWarning("Supported correction runtime unavailable; diagnostics disabled."); return; }
        Playthrough.Initialize(Config);
        _driver = AddComponent<Driver>();
        Log.LogInfo("Optional playthrough diagnostics ready. F10 toggles recording; F11 marks visible jitter. No GPU readback or native hooks. F8 is a separate optional FFVI-only legacy capture.");
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
