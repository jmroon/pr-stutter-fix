using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace PRStutter.UnroundedExperiment;

// This add-on owns texture bindings only. The stock movement DLL is unchanged.
[BepInPlugin("local.prstutter.resolutioncomparison", "PR Stutter Resolution Comparison", "0.1.0")]
[BepInDependency("local.prstutter.unrounded", "0.5.0")]
public sealed class ResolutionPlugin : BasePlugin
{
    private ResolutionDriver? _driver;
    public override void Load()
    {
        ResolutionHost.Log = Log;
        MovementReader.Initialize();
        ClassInjector.RegisterTypeInIl2Cpp<ResolutionPass>();
        _driver = AddComponent<ResolutionDriver>();
        ResolutionHost.Note("RESOLUTION COMPARISON READY: A stock; Alt+F11 switches B 8x. Enable stock smooth movement; CRT OFF. No timing/pacing writes.");
    }
    public override bool Unload()
    {
        if (!Resolution.Stop("unload") || !Resolution.ReleaseStopped()) return false;
        if (_driver != null) UnityEngine.Object.Destroy(_driver);
        return true;
    }
}

// Public, read-only contract for the independent audit.
public static class ResolutionStatus
{
    public static int RequestedRenderScale => Resolution.RequestedScale;
    public static int ResolutionCompletedFrames => Resolution.RequestedScale == 8 ? Resolution.CompletedFrames : 0;
    public static bool Faulted => Resolution.Faulted;
    public static string Status => Resolution.Note;
}

internal static class ResolutionHost
{
#if PR_FFIV
    public const string Game = "FFIV";
#else
    public const string Game = "FFVI";
#endif
    public static ManualLogSource? Log;
    public static void Note(string text)
    {
        Log?.LogInfo(text);
        try {
            string directory = Path.Combine(Paths.BepInExRootPath, "diagnostics/PRStutter");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "resolution-comparison.log"), $"{DateTime.UtcNow:o} {text}{Environment.NewLine}");
        } catch (Exception e) { Log?.LogWarning("Comparison log unavailable: " + e.Message); }
    }
    public static void Error(Exception e) { Log?.LogError(e); Note("RESOLUTION FAULT: " + e.Message); }
}

internal static class MovementReader
{
    private static PropertyInfo[]? _properties;
    public static void Initialize()
    {
        Type? type = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == "PRStutter.UnroundedExperiment")
                type = assembly.GetType("PRStutter.UnroundedExperiment.ExperimentStatus");
        if (type == null) throw new InvalidOperationException("Stock movement status is unavailable");
        string[] names = { "Enabled", "PrecisionActive", "TimingActive", "PacingActive", "Faulted",
            "ContextKind", "ContextIdentity", "Generation", "ContextField", "ContextMap", "ContextFrame" };
        _properties = new PropertyInfo[names.Length];
        for (int i=0; i<names.Length; i++) _properties[i] = type.GetProperty(names[i], BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing movement property: " + names[i]);
    }
    public static MovementGate Read()
    {
        T At<T>(int i) => (T)_properties![i].GetValue(null)!;
        return new(At<bool>(0),At<bool>(1),At<bool>(2),At<bool>(3),At<bool>(4),At<string>(5),At<string>(6),
            At<int>(7),At<long>(8),At<long>(9),At<int>(10));
    }
}

public sealed class ResolutionDriver : MonoBehaviour
{
    private MovementGate _started;
    private bool _request;
    private string _message = "A: STOCK 320x180";
    private long _nextReport;
    private GUIStyle? _style;
    public ResolutionDriver(IntPtr pointer) : base(pointer) { }
    private bool Guard()
    {
        var current = MovementReader.Read();
        if (Resolution.RequestedScale == 8 && (!current.Eligible(Time.frameCount) || !_started.SameContext(current))) {
            Resolution.Stop("movement context changed; returned to A"); return false;
        }
        return current.Eligible(Time.frameCount);
    }
    private void TickComparison()
    {
        try {
            Guard();
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool other = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool toggle = _request || (Input.GetKeyDown(KeyCode.F11) && alt && !other);
            _request = false;
            if (toggle) {
                if (Resolution.RequestedScale == 8) Resolution.Stop("manual A");
                else {
                    _started = MovementReader.Read();
                    if (_started.Eligible(Time.frameCount)) { Resolution.Toggle(_started.Field, _started.Map); _nextReport=0; }
                    else Resolution.Stop("Enable smooth movement and load ordinary walking before B");
                }
            }
            _message = Resolution.Faulted ? "RESTORATION FAULT: restart game" : Resolution.RequestedScale == 8 ?
                $"B: 8x 2560x1440 | completed frames: {Resolution.CompletedFrames}" : "A: STOCK 320x180 | " + Resolution.Note;
            if (Resolution.RequestedScale == 8 && Stopwatch.GetTimestamp() >= _nextReport) {
                _nextReport = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
                ResolutionHost.Note($"RESOLUTION SAMPLE scale=8 completedFrames={Resolution.CompletedFrames} context={_started.Identity}; timing/pacing/precision active");
            }
        } catch (Exception e) { Resolution.Stop("comparison error: " + e.Message); ResolutionHost.Error(e); }
    }
    public void Update() => Resolution.Recover();
    public void LateUpdate()
    {
        // All Update observers have run: do not reject a fresh field merely
        // because this add-on's Update precedes the movement driver's Update.
        try { TickComparison(); if (Guard()) Resolution.Prepare(); }
        catch (Exception e) { Resolution.Stop("comparison error: " + e.Message); }
    }
    public void OnGUI()
    {
        try {
            _style ??= new GUIStyle { fontSize = 20, wordWrap = true };
            _style.normal.textColor = Color.white;
            float width = Math.Min(620, Screen.width - 24), x = Screen.width - width - 12, y = Math.Max(12, Screen.height - 270);
            GUI.Box(new Rect(x,y,width,150), "");
            GUI.Label(new Rect(x+12,y+12,width-24,75),_message,_style);
            if (GUI.Button(new Rect(x+12,y+90,width-24,42),Resolution.RequestedScale==8 ? "Return to A: stock (Alt+F11)" : "Try B: 8x (Alt+F11)")) _request=true;
        } catch { }
    }
    public void OnApplicationFocus(bool focused) { if (!focused) Resolution.Stop("focus lost; returned to A"); }
    public void OnApplicationQuit() { Resolution.Stop("quit"); Resolution.ReleaseStopped(); }
}
