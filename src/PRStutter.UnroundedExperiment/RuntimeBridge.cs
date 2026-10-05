using System;
using System.Reflection;

namespace PRStutter.UnroundedExperiment;

internal static class RuntimeBridge
{
    private static Type? _type;
    private static PropertyInfo Property(string name) => _type?.GetProperty(name, BindingFlags.Static | BindingFlags.Public)
        ?? throw new InvalidOperationException("Timing comparison bridge missing: " + name);
    public static bool Active => _type != null && (bool)Property("Active").GetValue(null)!;
    public static bool PrecisionAllowed => _type != null && (bool)Property("PrecisionAllowed").GetValue(null)!;
    public static bool Faulted => _type != null && (bool)Property("Faulted").GetValue(null)!;
    public static bool TimingActive => _type != null && (bool)Property("TimingActive").GetValue(null)!;
    public static bool PacingActive => _type != null && (bool)Property("PacingActive").GetValue(null)!;
    public static string ContextKind => _type == null ? "off" : (string)Property("ContextKind").GetValue(null)!;
    public static string ContextIdentity => _type == null ? "off" : (string)Property("ContextIdentity").GetValue(null)!;
    public static string TimingStatus => _type == null ? "off" : (string)Property("TimingStatus").GetValue(null)!;
    public static string PacingStatus => _type == null ? "off" : (string)Property("PacingStatus").GetValue(null)!;
    public static int Generation => _type == null ? 0 : (int)Property("Generation").GetValue(null)!;
    public static long ContextField => _type == null ? 0 : (long)Property("ContextField").GetValue(null)!;
    public static long ContextMap => _type == null ? 0 : (long)Property("ContextMap").GetValue(null)!;
    public static int ContextArea => _type == null ? -1 : (int)Property("ContextArea").GetValue(null)!;
    public static int ContextFrame => _type == null ? -10 : (int)Property("ContextFrame").GetValue(null)!;
    public static int TimingSession => _type == null ? 0 : (int)Property("TimingSession").GetValue(null)!;
    public static void Refresh() => _type?.GetMethod("Refresh")!.Invoke(null, null);
    public static void Suspend(string reason) { if (_type != null) Invoke("Suspend", reason); }
    public static bool Healthy => _type != null && (bool)Property("Healthy").GetValue(null)!;
    public static bool Unrounded => _type != null && (bool)Property("UnroundedCarry").GetValue(null)!;
    public static int Carried => _type == null ? 0 : (int)Property("CarriedTiles").GetValue(null)!;
    public static string Status => _type == null ? "bridge not loaded" : (string)Property("Status").GetValue(null)!;
    public static void SetUnrounded(bool value) => Property("UnroundedCarry").SetValue(null, value);
    private static void Resolve()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == "PRStutter.TimingExperiment")
                _type = assembly.GetType("PRStutter.TimingExperiment.ComparisonControl");
        if (_type == null) throw new InvalidOperationException("Install timing 0.8.0 and grid 0.10.0 for smooth movement.");
    }
    public static void ManageControls(bool value)
    {
        Resolve(); Property("ManagedControls").SetValue(null, value);
    }
    public static void Start(long deadline)
    {
        Resolve(); Invoke("Start", deadline);
    }
    public static void Stop(string reason) { if (_type != null) Invoke("Stop", reason); }
    private static void Invoke(string name, object argument)
    {
        var method = _type?.GetMethod(name, BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException("Missing comparison method: " + name);
        try { method.Invoke(null, new[] { argument }); }
        catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
    }
}
