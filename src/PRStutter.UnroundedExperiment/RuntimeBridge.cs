using System;
using System.Reflection;

namespace PRStutter.UnroundedExperiment;

internal static class RuntimeBridge
{
    private static Type? _type;
    private static PropertyInfo Property(string name) => _type?.GetProperty(name, BindingFlags.Static | BindingFlags.Public)
        ?? throw new InvalidOperationException("Timing comparison bridge missing: " + name);
    public static bool Active => _type != null && (bool)Property("Active").GetValue(null)!;
    public static bool Healthy => _type != null && (bool)Property("Healthy").GetValue(null)!;
    public static bool Unrounded => _type != null && (bool)Property("UnroundedCarry").GetValue(null)!;
    public static int Carried => _type == null ? 0 : (int)Property("CarriedTiles").GetValue(null)!;
    public static string Status => _type == null ? "bridge not loaded" : (string)Property("Status").GetValue(null)!;
    public static void SetUnrounded(bool value) => Property("UnroundedCarry").SetValue(null, value);
    public static void Start(long deadline)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == "PRStutter.TimingExperiment")
                _type = assembly.GetType("PRStutter.TimingExperiment.ComparisonControl");
        if (_type == null) throw new InvalidOperationException("Install timing 0.6.3 and grid 0.9.2 for this comparison.");
        Invoke("Start", deadline);
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
