using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace PRStutter.UnroundedExperiment;

// Read-only reflection contract for the independent audit. Faults never masquerade as OFF.
public static class ExperimentStatus { public static string State => Experiment.State; }

[BepInPlugin("local.prstutter.unrounded", "PR Stutter Unrounded Movement Test", "0.1.0")]
public sealed class Plugin : BasePlugin
{
    private Driver? _driver;
    public override void Load()
    {
        Experiment.Log = Log;
        try {
            Experiment.Initialize(); _driver = AddComponent<Driver>();
            Log.LogInfo($"{Experiment.Game} unrounded movement ready, OFF. Shift+F11 toggles for 120s; Ctrl+F11 records with audit 0.1.1. Keep F9/F10 off. Only reviewed movement rounding calls are bypassed in process memory; no disk game edits.");
        } catch (Exception e) { Experiment.Fault(e); }
    }
    public override bool Unload()
    {
        if (!Experiment.Stop("unload")) return false;
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
    private static PropertyInfo? _runtimeCurrent;
    private static PropertyInfo[]? _flags;
    private static bool _runtimePresent;
    private static bool _fault;
    private static long _deadline;
    private static string _note = "starting";
    public static string State => _fault || _patches?.Faulted == true ? "fault" : _patches?.Enabled == true ? "on" : "off";
    public static string Label => $"UNROUNDED MOVEMENT {State.ToUpperInvariant()} | Shift+F11 toggle | " +
        (State == "on" ? $"{Math.Max(0, (_deadline - Stopwatch.GetTimestamp()) / Stopwatch.Frequency)}s remaining" : _note);
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
    private static void FindRuntime()
    {
        _runtimePresent = false; _runtimeCurrent = null; _flags = null;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) {
            if (a.GetName().Name != "PRStutter.TimingExperiment") continue;
            _runtimePresent = true;
            _runtimeCurrent = a.GetType("PRStutter.TimingExperiment.CorrectionStatus")?.GetProperty("Current",BindingFlags.Public | BindingFlags.Static);
            var type = _runtimeCurrent?.PropertyType;
            if (type == null) return;
            var flags = new PropertyInfo[4]; int i = 0;
            foreach (var name in new[] { "Enabled", "Timing", "Pacing", "Smoothing" }) {
                var prop = type.GetProperty(name); if (prop?.PropertyType != typeof(bool)) return;
                flags[i++] = prop;
            }
            _flags = flags;
        }
    }
    private static bool Compatible()
    {
        if (!_runtimePresent) return true;
        var snapshot = _runtimeCurrent?.GetValue(null);
        if (snapshot == null || _flags == null) return false;
        foreach (var flag in _flags) if ((bool)flag.GetValue(snapshot)!) return false;
        return true;
    }
    public static void Tick()
    {
        try {
            if (State == "on" && (!Compatible() || Stopwatch.GetTimestamp() >= _deadline)) Stop("timeout or other corrections enabled");
            if (!Input.GetKeyDown(KeyCode.F11) || !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ||
                Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return;
            if (State == "on") { Stop("manual"); return; }
            if (State == "fault" || _patches == null) return;
            FindRuntime();
            if (!Compatible()) { _note = "refused: turn F9 corrections OFF"; Log?.LogWarning(_note); return; }
            _patches.Set(true); _deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;
            Log?.LogInfo("UNROUNDED ON: " + PatchPlan.For(Game).Length + " movement calls bypassed; 120 seconds.");
        } catch (Exception e) { Fault(e); }
    }
    public static bool Stop(string reason)
    {
        try {
            bool wasOn = _patches?.Enabled == true;
            _patches?.Restore(); _note = "native rounding restored (" + reason + ")";
            if (wasOn) Log?.LogInfo("UNROUNDED OFF: " + reason + "; owned calls restored.");
            return _patches?.HasOwnedSites != true;
        } catch (Exception e) { _fault = true; _note = "RESTORE FAILED; restart game"; Log?.LogError(e); return false; }
    }
    public static void Fault(Exception e) { _fault = true; Stop("fault"); _note = "FAULT; restart game"; Log?.LogError(e); }
}

public sealed class Driver : MonoBehaviour
{
    public Driver(IntPtr pointer) : base(pointer) { }
    public void Update() => Experiment.Tick();
    public void OnGUI() { try { GUI.Label(new Rect(12, Screen.height - 135, Math.Max(200, Screen.width - 24), 40), Experiment.Label); } catch { } }
    public void OnApplicationFocus(bool focus) { if (!focus) Experiment.Stop("focus lost"); }
    public void OnApplicationQuit() => Experiment.Stop("quit");
}
