using System;
using System.Reflection;

namespace PRStutter.PresentationAudit;

// Value-only observation contract. No compile-time correction dependency and no
// way to enable, suspend or fail a correction from the diagnostic component.
internal sealed record RuntimeState(bool Enabled, bool PrecisionActive, bool TimingActive,
    bool PacingActive, bool Faulted, string ContextKind, string ContextIdentity,
    string TimingStatus, string PacingStatus, int Generation, int TimingSession,
    long ContextField, long ContextMap, int ContextArea, int ContextFrame, string ComparisonMode)
{
    public string Condition(long field, long map, int area, int frame)
    {
        if (Faulted) return "stock-invalid";
        if (!Enabled) return ComparisonMode == "off" && !PrecisionActive ? "off" : "stock-invalid";
        if (ComparisonMode == "stock-suspended")
            return !PrecisionActive ? "stock-suspended" : "stock-invalid";
        if (!PrecisionActive) return "stock-invalid";
        bool valid = ComparisonMode switch {
            "stock-manual" => TimingActive && PacingActive,
            "stock-scripted" => !TimingActive && PacingActive,
            "stock-precision-only" => !TimingActive && !PacingActive,
            _ => false
        };
        if (!valid) return "stock-invalid";
        if (ContextField == 0 || ContextMap == 0 || ContextField != field || ContextMap != map ||
            ContextArea != area || frame - ContextFrame is < 0 or > 1) return "stock-context-mismatch";
        return ComparisonMode;
    }
}

internal sealed class RuntimeReader
{
    private readonly PropertyInfo[] _properties;
    public RuntimeReader(Type type)
    {
        string[] names = { "Enabled", "PrecisionActive", "TimingActive", "PacingActive", "Faulted",
            "ContextKind", "ContextIdentity", "TimingStatus", "PacingStatus", "Generation", "TimingSession",
            "ContextField", "ContextMap", "ContextArea", "ContextFrame", "ComparisonMode" };
        _properties = new PropertyInfo[names.Length];
        for (int i = 0; i < names.Length; i++) _properties[i] = type.GetProperty(names[i], BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Incomplete stock status contract: " + names[i]);
    }
    public RuntimeState Read()
    {
        T At<T>(int i) => (T)_properties[i].GetValue(null)!;
        return new(At<bool>(0), At<bool>(1), At<bool>(2), At<bool>(3), At<bool>(4),
            At<string>(5), At<string>(6), At<string>(7), At<string>(8), At<int>(9), At<int>(10),
            At<long>(11), At<long>(12), At<int>(13), At<int>(14), At<string>(15));
    }
}
