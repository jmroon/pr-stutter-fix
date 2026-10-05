using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Last.Map;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace PRStutter.UnroundedExperiment;

// Rendering owns texture bindings only and reads the movement/settings contracts.
[BepInPlugin("local.prstutter.resolutioncomparison", "PR Stutter Rendering", "0.3.0")]
[BepInDependency("local.prstutter.unrounded", "0.6.0")]
public sealed class ResolutionPlugin : BasePlugin
{
    private ResolutionDriver? _driver;
    public override void Load()
    {
        ResolutionHost.Log = Log;
        MovementReader.Initialize();
        ClassInjector.RegisterTypeInIl2Cpp<ResolutionPass>();
        _driver = AddComponent<ResolutionDriver>();
        ResolutionHost.Note("RENDERING READY: follows saved menu settings; resumes in supported walking scenes after validation. CRT OFF for 4x/8x. No timing/pacing writes.");
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
    public static int ResolutionCompletedFrames => Resolution.RequestedScale > 1 ? Resolution.CompletedFrames : 0;
    public static bool Faulted => Resolution.Faulted || ResolutionDriver.GuardFaulted;
    public static string Status => ResolutionDriver.Status;
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
    private static PropertyInfo? _scale, _revision;
    public static int Scale => (int)_scale!.GetValue(null)!;
    public static int Revision => (int)_revision!.GetValue(null)!;
    public static void Initialize()
    {
        Type? type = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == "PRStutter.UnroundedExperiment")
                type = assembly.GetType("PRStutter.UnroundedExperiment.ExperimentStatus");
        if (type == null) throw new InvalidOperationException("Stock movement status is unavailable");
        var settings = type.Assembly.GetType("PRStutter.UnroundedExperiment.SettingsStatus")
            ?? throw new InvalidOperationException("Settings contract unavailable");
        _scale = settings.GetProperty("RenderScale") ?? throw new MissingMemberException("RenderScale");
        _revision = settings.GetProperty("Revision") ?? throw new MissingMemberException("Revision");
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
    private readonly ResolutionReadiness<RenderContext> _readiness = new();
    private RenderContext? _activeKey;
    private bool _guardFault;
    public static bool GuardFaulted { get; private set; }
    public static string Status { get; private set; } = "Waiting for supported scene";
    public ResolutionDriver(IntPtr pointer) : base(pointer) { }
    public void Update() => Resolution.Recover();
    public void LateUpdate()
    {
        if (_guardFault) return;
        // Read after core Update has refreshed context and processed menu intent.
        try {
            var current = MovementReader.Read();
            int desired = MovementReader.Scale;
            bool eligible = desired > 1 && current.Eligible(Time.frameCount) && Application.isFocused && Time.timeScale == 1;
            string reason = !current.Enabled ? "OFF" : desired == 1 ? "Native (selected)" : "Native - waiting for supported walking scene";
            long materialId = 0; float mainBias = 0, overlayBias = 0;
            if (eligible) {
                var material = PostProcessLite.GetMaterial();
                if (material == null || material.shader.name != "Last/PostProcessLite") { eligible = false; reason = "Native - unsupported compositor"; }
                else if (material.GetFloat("_FakeCRT") != 0) { eligible = false; reason = "Native - turn CRT off to use 4x/8x"; }
                else if (material.GetFloat("_BlurMainGame") != 0 || material.GetFloat("_PartialFadeOverlay") != 0) {
                    eligible = false; reason = "Native - scene transition/effect";
                } else {
                    materialId = material.Pointer.ToInt64();
                    mainBias = material.GetFloat("_MainGameDiffuseBias"); overlayBias = material.GetFloat("_OverlayDiffuseBias");
                }
            }
            var key = new RenderContext(current.Identity, current.Generation, current.Field, current.Map,
                desired, MovementReader.Revision, Screen.width, Screen.height, materialId, mainBias, overlayBias);
            if (Resolution.RequestedScale > 1 && (!eligible || key != _activeKey)) Resolution.Stop("settings or scene changed");
            bool attempt = _readiness.ShouldStart(key, eligible, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, Time.frameCount);
            if (Resolution.Faulted) { Status = "Cleanup fault - restart game"; return; }
            if (attempt && Resolution.RequestedScale == 1) { _activeKey = key; Resolution.Select(desired, current.Field, current.Map); }
            if (eligible) Resolution.Prepare();
            Status = Resolution.Faulted ? "Cleanup fault - restart game" : Resolution.RequestedScale > 1 ?
                (Resolution.RequestedScale == 4 ? "4x active" : "8x active") : !eligible ? reason :
                _activeKey != key ? "Native - waiting for stable scene" : "Native - " + Resolution.Note;
        } catch (Exception e) {
            _guardFault = GuardFaulted = true;
            Resolution.Stop("rendering guard: " + e.Message);
            Status = "Rendering guard fault - restart game: " + e.Message;
            ResolutionHost.Error(e);
        }
    }
    public void OnApplicationFocus(bool focused)
    {
        if (!focused) { _readiness.Reset(); if (Resolution.RequestedScale > 1) Resolution.Stop("focus lost"); }
    }
    public void OnApplicationQuit() { Resolution.Stop("quit"); Resolution.ReleaseStopped(); }
}
