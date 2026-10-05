using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace PRStutter.UnroundedExperiment;

// Read-only reflection contract for the independent audit. Faults never masquerade as OFF.
public static class ExperimentStatus
{
    public static bool Enabled => Experiment.Running;
    public static bool PrecisionActive => Experiment.State == "on";
    public static bool TimingActive => RuntimeBridge.TimingActive;
    public static bool PacingActive => RuntimeBridge.PacingActive;
    public static bool Faulted => Experiment.State == "fault" || RuntimeBridge.Faulted;
    public static string ContextKind => RuntimeBridge.ContextKind;
    public static string ContextIdentity => RuntimeBridge.ContextIdentity;
    public static string TimingStatus => RuntimeBridge.TimingStatus;
    public static string PacingStatus => RuntimeBridge.PacingStatus;
    public static int Generation => RuntimeBridge.Generation;
    public static long ContextField => RuntimeBridge.ContextField;
    public static long ContextMap => RuntimeBridge.ContextMap;
    public static int ContextArea => RuntimeBridge.ContextArea;
    public static int ContextFrame => RuntimeBridge.ContextFrame;
    public static int TimingSession => RuntimeBridge.TimingSession;
    public static string State => Experiment.State;
    public static string ComparisonMode => Experiment.ComparisonMode;
    public static int CarriedTiles => RuntimeBridge.Carried;
    public static int RequestedRenderScale => 1;
    public static int ResolutionCompletedFrames => 0;
}

[BepInPlugin("local.prstutter.unrounded", "PR Stutter Fix", "0.6.0")]
[BepInDependency("local.prstutter.timing", "0.8.0")]
public sealed class Plugin : BasePlugin
{
    private Driver? _driver;
    public override void Load()
    {
        Experiment.Log = Log;
        try {
            Experiment.Initialize(); RuntimeBridge.ManageControls(true); SettingsMenu.Initialize();
            _driver = AddComponent<Driver>(); Experiment.RequestEnabled(SettingsMenu.Current.Enabled);
            Log.LogInfo($"{Experiment.Game} smooth movement 0.6.0 ready. {SettingsMenu.Current.MenuKey}: settings menu; settings save per game. Diagnostics optional.");
        } catch (Exception e) { Experiment.Fault(e); }
    }
    public override bool Unload()
    {
        if (!Experiment.Stop("unload")) return false;
        SettingsMenu.SetVisible(false); RuntimeBridge.ManageControls(false);
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
    private static bool _fault, _running;
    private static bool? _enabledRequest;
    private static string _note = "starting";
    public static bool Running => _running;
    public static string State => _fault || _patches?.Faulted == true ? "fault" : _patches?.Enabled == true ? "on" : "off";
    public static string ComparisonMode => !_running ? "off" : !RuntimeBridge.Healthy || State == "fault" ? "stock-invalid" :
        _patches?.Enabled != true ? "stock-suspended" : RuntimeBridge.PacingActive ?
        RuntimeBridge.TimingActive ? "stock-manual" : "stock-scripted" : "stock-precision-only";
    public static string Label => _running ?
        "SMOOTH MOVEMENT ENABLED | precision " + (_patches?.Enabled == true ? "ON" : "suspended") + "\n" + RuntimeBridge.Status :
        "SMOOTH MOVEMENT " + (State == "fault" ? "FAULT" : "OFF") + "\n" + _note;
    // Unity GUI events only queue work; native patches change during Update.
    public static void RequestEnabled(bool enabled) => _enabledRequest = enabled;
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
            if (_running) {
                RuntimeBridge.Refresh();
                if (!RuntimeBridge.Healthy) { Fault(new InvalidOperationException(RuntimeBridge.Status)); return; }
                bool precision = RuntimeBridge.PrecisionAllowed;
                if (_patches!.Enabled != precision) {
                    _patches.Set(precision);
                    Log?.LogInfo("PRECISION " + (precision ? "ON" : "SUSPENDED") + ": " + RuntimeBridge.ContextKind);
                }
                RuntimeBridge.SetUnrounded(precision);
            }
            if (!_enabledRequest.HasValue) return;
            bool enabled = _enabledRequest.Value; _enabledRequest = null;
            if (!enabled) { Stop("disabled in settings"); return; }
            if (_running) return;
            if (State == "fault" || _patches == null) return;
            try {
                RuntimeBridge.Start(long.MaxValue);
                _running = true;
                Log?.LogInfo("STOCK MOVEMENT ENABLED: native resolution; waiting for supported context; no timeout.");
            } catch (Exception e) { Fault(new InvalidOperationException("Start refused: " + e.Message, e)); }
        } catch (Exception e) { Fault(e); }
    }
    public static bool Stop(string reason)
    {
        bool wasRunning = _running || RuntimeBridge.Active;
        _running = false; _enabledRequest = null;
        bool ok = true;
        // A native restore failure must not block timing/pacing cleanup.
        try { _patches?.Restore(); } catch (Exception e) { ok = false; Log?.LogError(e); }
        try { RuntimeBridge.Stop(reason); } catch (Exception e) { ok = false; Log?.LogError(e); }
        if (_patches?.HasOwnedSites == true || RuntimeBridge.Active) ok = false;
        _fault |= !ok;
        _note = ok ? reason : "RESTORE FAILED; restart game";
        if (wasRunning || !ok) Log?.LogInfo("STOCK MOVEMENT OFF: " + _note);
        return ok;
    }
    public static void SuspendForFocus()
    {
        if (!_running) return;
        try { RuntimeBridge.Suspend("focus lost"); _patches?.Restore(); RuntimeBridge.SetUnrounded(false); }
        catch (Exception e) { Fault(e); }
    }
    public static void Fault(Exception e) { _fault = true; Stop("fault"); _note = "FAULT; restart game"; Log?.LogError(e); }
}

public sealed class Driver : MonoBehaviour
{
    public Driver(IntPtr pointer) : base(pointer) { }
    private bool _menuFailed;
    public void Update()
    {
        // UI and persistence failures must not take down movement corrections.
        try { SettingsMenu.Tick(); } catch (Exception e) { ReportMenuFailure(e); }
        Experiment.Tick();
    }
    public void OnGUI() { try { SettingsMenu.Draw(); } catch (Exception e) { ReportMenuFailure(e); } }
    private void ReportMenuFailure(Exception e)
    {
        if (!_menuFailed) Experiment.Log?.LogWarning("Settings menu error: " + e);
        _menuFailed = true;
    }
    public void OnApplicationFocus(bool focus)
    {
        if (!focus) {
            try { SettingsMenu.SetVisible(false); } catch (Exception e) { ReportMenuFailure(e); }
            Experiment.SuspendForFocus();
        }
    }
    public void OnApplicationQuit()
    {
        try { SettingsMenu.SetVisible(false); } catch (Exception e) { ReportMenuFailure(e); }
        Experiment.Stop("quit");
    }
}
