using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace PRStutter.UnroundedExperiment;

// Read-only reflection contract for the independent audit. Faults never masquerade as OFF.
public static class ExperimentStatus
{
    public static string State => Experiment.State;
    public static string ComparisonMode => Experiment.ComparisonMode;
    public static int CarriedTiles => RuntimeBridge.Carried;
    public static int RequestedRenderScale => Resolution.RequestedScale;
    public static int ResolutionCompletedFrames => Resolution.CompletedFrames;
}

[BepInPlugin("local.prstutter.unrounded", "PR Stutter Unrounded Movement Test", "0.3.0")]
public sealed class Plugin : BasePlugin
{
    private Driver? _driver;
    public override void Load()
    {
        Experiment.Log = Log;
        try {
            Experiment.Initialize(); ClassInjector.RegisterTypeInIl2Cpp<ResolutionPass>(); _driver = AddComponent<Driver>();
            Log.LogInfo($"{Experiment.Game} comparison 0.3.0 ready, OFF. Shift+F11 starts A/stops; Alt+F11 switches A/B; Ctrl+F11 records. A=timing+pacing, B=timing+pacing+unrounded+8x. CRT OFF. Old compensation OFF in both.");
        } catch (Exception e) { Experiment.Fault(e); }
    }
    public override bool Unload()
    {
        if (!Experiment.Stop("unload")) return false;
        if (!Resolution.ReleaseStopped()) return false;
        if (_driver != null) UnityEngine.Object.Destroy(_driver);
        return true;
    }
}

internal static class Experiment
{
#if PR_FFIV
    public const string Game = "FFIV", Roman = "IV";
    private const string AssemblyHash = "bb2f4c9db44c8ee9b065696aeafb77561a1709492130f045433e6d215abc42ed";
    private const string MetadataHash = "37400ca079eddb18cda06450c02c7bd8eb2c057175502e75e2e2c8bc09b31f41";
#else
    public const string Game = "FFVI", Roman = "VI";
    private const string AssemblyHash = "0029a22ed933aa3b6ea3b1290060502267f514d61ba6e6619308d844557f2ffd";
    private const string MetadataHash = "f50d9d1ff84f8033b8acbdc6845ab3a0f2793dd253c36a4a7d212303980844dd";
#endif
    public static ManualLogSource? Log;
    private static PatchSet? _patches;
    private static bool _fault, _running, _modeB;
    private static long _deadline;
    private static string _note = "starting";
    public static string State => _fault || Resolution.Faulted || _patches?.Faulted == true ? "fault" : _patches?.Enabled == true ? "on" : "off";
    public static string ComparisonMode => !_running ? "off" : State == "fault" || !RuntimeBridge.Healthy ? "comparison-invalid" :
        _modeB && _patches?.Enabled == true && Resolution.RequestedScale == 8 && RuntimeBridge.Unrounded ? "timing-pacing-unrounded-8x" :
        !_modeB && _patches?.Enabled != true && Resolution.RequestedScale == 1 && !RuntimeBridge.Unrounded ? "timing-pacing" : "comparison-invalid";
    public static string Label => _running ?
        $"COMPARISON {(_modeB ? "B: timing + pacing + unrounded + 8x" : "A: timing + pacing ONLY")} | {ComparisonMode} | carried {RuntimeBridge.Carried} | {Math.Max(0, (_deadline - Stopwatch.GetTimestamp()) / Stopwatch.Frequency)}s\nShift+F11 STOP | Alt+F11 A/B | Ctrl+F11 record | compensation OFF" :
        "COMPARISON OFF | Shift+F11 starts A | " + _note;
    public static void Initialize()
    {
        if (!Environment.Is64BitProcess) throw new NotSupportedException("x64 required");
        void Match(string relative, string expected) {
            using var file = File.OpenRead(Path.Combine(Paths.GameRootPath, relative));
            using var hash = SHA256.Create();
            if (!Convert.ToHexString(hash.ComputeHash(file)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsupported game binary: " + relative);
        }
        Match("GameAssembly.dll", AssemblyHash);
        Match($"FINAL FANTASY {Roman}_Data/il2cpp_data/Metadata/global-metadata.dat", MetadataHash);
        using var process = Process.GetCurrentProcess();
        ProcessModule? module = null;
        foreach (ProcessModule candidate in process.Modules) if (string.Equals(candidate.ModuleName,"GameAssembly.dll", StringComparison.OrdinalIgnoreCase)) module = candidate;
        if (module == null || module.FileName == null || !Path.GetFullPath(module.FileName).Equals(Path.GetFullPath(Path.Combine(Paths.GameRootPath,"GameAssembly.dll")),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected loaded GameAssembly module");
        _patches = new PatchSet(new ProcessCodeMemory(module.BaseAddress, module.ModuleMemorySize), PatchPlan.For(Game));
        _note = "native rounding; test has not started";
    }
    public static void Tick()
    {
        try {
            Resolution.Recover();
            if (Resolution.Faulted && !_fault) { Fault(new InvalidOperationException("Resolution cleanup failed; restart game")); return; }
            if (_running && (ComparisonMode == "comparison-invalid" || Stopwatch.GetTimestamp() >= _deadline)) {
                Stop("component stopped or timeout: " + RuntimeBridge.Status + "; resolution=" + Resolution.Note); return;
            }
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (!Input.GetKeyDown(KeyCode.F11) || control) return;
            if (alt && !shift && _running) { Switch(); return; }
            if (!shift || alt) return;
            if (_running) { Stop("manual"); return; }
            if (State == "fault" || _patches == null) return;
            _deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;
            try {
                RuntimeBridge.Start(_deadline); _running = true; _modeB = false;
                Log?.LogInfo("COMPARISON A: " + RuntimeBridge.Status + "; native rounding, stock resolution; 120s.");
            } catch (Exception e) { Stop("start refused: " + e.Message); }
        } catch (Exception e) { Fault(e); }
    }
    private static void Switch()
    {
        if (!RuntimeBridge.Healthy) { Stop("timing/pacing unavailable"); return; }
        if (_modeB) {
            if (!Resolution.Stop("comparison A")) throw new InvalidOperationException("Resolution restoration failed");
            _patches!.Restore(); RuntimeBridge.SetUnrounded(false); _modeB = false;
        } else {
            _patches!.Set(true); RuntimeBridge.SetUnrounded(true);
            Resolution.Toggle();
            if (Resolution.RequestedScale != 8) { Stop("B refused: " + Resolution.Note); return; }
            _modeB = true;
        }
        Log?.LogInfo("COMPARISON " + (_modeB ? "B" : "A") + ": " + RuntimeBridge.Status + "; " + ComparisonMode);
    }
    public static bool Stop(string reason)
    {
        bool wasRunning = _running || RuntimeBridge.Active;
        _running = false; _modeB = false;
        bool ok = true;
        // Independent cleanup attempts: a texture restore failure must not leave
        // timing, pacing or native call patches running.
        try { if (!Resolution.Stop("unrounded stop")) ok = false; } catch (Exception e) { ok = false; Log?.LogError(e); }
        try { _patches?.Restore(); } catch (Exception e) { ok = false; Log?.LogError(e); }
        try { RuntimeBridge.Stop(reason); } catch (Exception e) { ok = false; Log?.LogError(e); }
        if (_patches?.HasOwnedSites == true || RuntimeBridge.Active) ok = false;
        _fault |= !ok;
        _note = ok ? reason : "RESTORE FAILED; restart game";
        if (wasRunning || !ok) Log?.LogInfo("COMPARISON OFF: " + _note);
        return ok;
    }
    public static void Fault(Exception e) { _fault = true; Stop("fault"); _note = "FAULT; restart game"; Log?.LogError(e); }
}

public sealed class Driver : MonoBehaviour
{
    public Driver(IntPtr pointer) : base(pointer) { }
    public void Update() => Experiment.Tick();
    public void LateUpdate() => Resolution.Prepare();
    public void OnGUI() { try {
        GUI.Label(new Rect(12, Screen.height - 185, Math.Max(200, Screen.width - 24), 55), Experiment.Label);
        GUI.Label(new Rect(12, Screen.height - 135, Math.Max(200, Screen.width - 24), 40), Resolution.Label);
    } catch { } }
    public void OnApplicationFocus(bool focus) { if (!focus) Experiment.Stop("focus lost"); }
    public void OnApplicationQuit() => Experiment.Stop("quit");
}
