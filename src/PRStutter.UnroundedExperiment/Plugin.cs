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
    public static string State => Experiment.State;
    public static string ComparisonMode => Experiment.ComparisonMode;
    public static int CarriedTiles => RuntimeBridge.Carried;
    public static int RequestedRenderScale => 1;
    public static int ResolutionCompletedFrames => 0;
}

[BepInPlugin("local.prstutter.unrounded", "PR Stutter Stock Movement", "0.4.0")]
public sealed class Plugin : BasePlugin
{
    private Driver? _driver;
    public override void Load()
    {
        Experiment.Log = Log;
        try {
            Experiment.Initialize(); _driver = AddComponent<Driver>();
            Log.LogInfo($"{Experiment.Game} stock movement 0.4.0 ready, OFF. Smooth walking button or Shift+F11 toggles; Ctrl+F11 records. Timing+pacing+unrounded; native rendering, no timeout. Control changes still stop this preview; no automatic restart.");
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
    private static bool _fault, _running, _toggleRequested;
    private static string _note = "starting";
    public static bool Running => _running;
    public static string State => _fault || _patches?.Faulted == true ? "fault" : _patches?.Enabled == true ? "on" : "off";
    public static string ComparisonMode => !_running ? "off" : State == "fault" || !RuntimeBridge.Healthy ||
        _patches?.Enabled != true || !RuntimeBridge.Unrounded ? "comparison-invalid" : "timing-pacing-unrounded-stock";
    public static string Label => _running ?
        "SMOOTH WALKING ON | native resolution\n" + RuntimeBridge.Status :
        "SMOOTH WALKING " + (State == "fault" ? "FAULT" : "OFF") + "\n" + _note;
    // Unity GUI events only queue work; native patches change during Update.
    public static void RequestToggle() => _toggleRequested = true;
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
            if (_running && ComparisonMode == "comparison-invalid") {
                Stop("component stopped: " + RuntimeBridge.Status); return;
            }
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool toggle = _toggleRequested || (Input.GetKeyDown(KeyCode.F11) && shift && !control && !alt);
            _toggleRequested = false;
            if (!toggle) return;
            if (_running) { Stop("manual"); return; }
            if (State == "fault" || _patches == null) return;
            try {
                RuntimeBridge.Start(long.MaxValue);
                _patches.Set(true); RuntimeBridge.SetUnrounded(true);
                _running = true;
                Log?.LogInfo("STOCK MOVEMENT ON: " + RuntimeBridge.Status + "; unrounded movement, native resolution; no timeout.");
            } catch (Exception e) { Stop("start refused: " + e.Message); }
        } catch (Exception e) { Fault(e); }
    }
    public static bool Stop(string reason)
    {
        bool wasRunning = _running || RuntimeBridge.Active;
        _running = false; _toggleRequested = false;
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
    public static void Fault(Exception e) { _fault = true; Stop("fault"); _note = "FAULT; restart game"; Log?.LogError(e); }
}

public sealed class Driver : MonoBehaviour
{
    public Driver(IntPtr pointer) : base(pointer) { }
    public void Update() => Experiment.Tick();
    private GUIStyle? _labelStyle, _buttonStyle;
    private GUIContent? _content;
    private long _nextRefresh;
    private float _labelHeight, _lastWidth;
    private int _lastFlags = -1, _lastFont;
    private bool _panelFailed;
    public void OnGUI()
    {
        if (_panelFailed) return;
        try {
            _labelStyle ??= new GUIStyle { wordWrap = true, alignment = TextAnchor.UpperLeft };
            _buttonStyle ??= new GUIStyle { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            _labelStyle.normal.textColor = _buttonStyle.normal.textColor = Color.white;
            int font = Math.Clamp(Screen.height / 65, 16, 22);
            _labelStyle.fontSize = _buttonStyle.fontSize = font;
            float width = Math.Min(620, Screen.width - 24), x = Screen.width - width - 12;
            long now = Stopwatch.GetTimestamp();
            int flags = (Experiment.Running ? 1 : 0) | (Experiment.State == "fault" ? 2 : 0);
            if (_content == null || now >= _nextRefresh || flags != _lastFlags || width != _lastWidth || font != _lastFont) {
                _content ??= new GUIContent();
                _content.text = Experiment.Label;
                _labelHeight = _labelStyle.CalcHeight(_content, width - 24);
                _nextRefresh = now + Stopwatch.Frequency / 10;
                _lastFlags = flags; _lastWidth = width; _lastFont = font;
            }
            float buttonY = 24 + _labelHeight;
            GUI.Box(new Rect(x, 12, width, _labelHeight + font * 3 + 60), "");
            GUI.Label(new Rect(x + 12, 24, width - 24, _labelHeight), _content, _labelStyle);
            var button = new Rect(x + 12, buttonY + 8, width - 24, font + 24);
            if (GUI.Button(button, "")) Experiment.RequestToggle();
            GUI.Label(button, Experiment.Running ? "Turn smooth walking OFF" : "Enable smooth walking (stock resolution)", _buttonStyle);
            GUI.Label(new Rect(x + 12, buttonY + font + 40, width - 24, font + 12),
                "Shift+F11: toggle | Ctrl+F11: record", _labelStyle);
        } catch (Exception e) {
            _panelFailed = true;
            Experiment.Log?.LogWarning("Smooth walking panel unavailable; Shift+F11 still works: " + e.Message);
        }
    }
    public void OnApplicationFocus(bool focus) { if (!focus) Experiment.Stop("focus lost"); }
    public void OnApplicationQuit() => Experiment.Stop("quit");
}
