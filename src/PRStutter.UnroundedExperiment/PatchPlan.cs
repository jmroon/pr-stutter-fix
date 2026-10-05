using System;
using System.Linq;

namespace PRStutter.UnroundedExperiment;

internal sealed record CallSite(int Rva, string Name, string Before, string Call, string After)
{
    public byte[] Original => Convert.FromHexString(Call);
}

internal static class PatchPlan
{
    public static readonly byte[] Bypass = { 0x90, 0x90, 0x90, 0x90, 0x90 };
    public static CallSite[] For(string game) => game switch {
        "FFVI" => new[] {
            new CallSite(0xFEA17F,"FieldEntity.UpdateMovingSetPosition.X","58F80F28C7","E84CE50FFF","F30F1107F3"),
            new CallSite(0xFEA19B,"FieldEntity.UpdateMovingSetPosition.Y","F30F58435C","E830E50FFF","488B5C2460"),
            new CallSite(0xFF3EEC,"FieldPlayer.UpdateMovingSetPosition.X","58F80F28C7","E8DF470FFF","F30F1107F3"),
            new CallSite(0xFF3F08,"FieldPlayer.UpdateMovingSetPosition.Y","F30F58435C","E8C3470FFF","F30F114704") },
        "FFIV" => new[] {
            new CallSite(0xBDCE0F,"FieldEntity.UpdateEntity.X","58F80F28C7","E89C1350FF","F30F114424"),
            new CallSite(0xBDCE2D,"FieldEntity.UpdateEntity.Y","F30F58435C","E87E1350FF","33D2F30F11") },
        _ => throw new NotSupportedException(game)
    };
}

internal interface ICodeMemory
{
    byte[] Read(int rva, int length);
    void Write(int rva, byte[] bytes);
}

// These inspected movement paths run on Unity's main thread. Mutations are only
// allowed from that same thread, between native movement calls. This is not a
// general-purpose concurrent code patcher and must not be used for worker code.
internal sealed class PatchSet
{
    private readonly ICodeMemory _memory;
    private readonly CallSite[] _sites;
    private readonly bool[] _owned;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    public bool Enabled { get; private set; }
    public bool Faulted { get; private set; }
    public bool HasOwnedSites => _owned.Any(x => x);
    public PatchSet(ICodeMemory memory, CallSite[] sites)
    {
        _memory = memory; _sites = sites; _owned = new bool[sites.Length];
        if (sites.Length == 0 || sites.Select(s => s.Rva).Distinct().Count() != sites.Length)
            throw new ArgumentException("Invalid patch sites");
        foreach (var site in sites) {
            if (site.Original.Length != 5 || site.Original[0] != 0xE8) throw new ArgumentException("Expected rel32 CALL");
        }
        Validate(false);
    }
    private void ThreadCheck()
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Patch must run on its owning Unity thread");
    }
    private void Expect(int rva, byte[] expected)
    {
        if (!_memory.Read(rva, expected.Length).SequenceEqual(expected)) throw new InvalidOperationException($"Unexpected code at RVA 0x{rva:X}; refusing overwrite");
    }
    private void Validate(bool enabled)
    {
        foreach (var site in _sites) {
            Expect(site.Rva - 5, Convert.FromHexString(site.Before));
            Expect(site.Rva, enabled ? PatchPlan.Bypass : site.Original);
            Expect(site.Rva + 5, Convert.FromHexString(site.After));
        }
    }
    public void Set(bool enabled)
    {
        ThreadCheck();
        if (Faulted) throw new InvalidOperationException("Patch fault is latched; restart required");
        try {
            Validate(Enabled); // Preflight ALL sites before touching any.
            if (enabled == Enabled) return;
            for (int i = 0; i < _sites.Length; i++) {
                // Retain ownership even if Write throws after changing memory.
                if (enabled) _owned[i] = true;
                _memory.Write(_sites[i].Rva, enabled ? PatchPlan.Bypass : _sites[i].Original);
                Expect(_sites[i].Rva, enabled ? PatchPlan.Bypass : _sites[i].Original);
                if (!enabled) _owned[i] = false;
            }
            Enabled = enabled;
        } catch (Exception error) {
            Faulted = true; Enabled = false;
            try { Restore(); } catch (Exception rollback) { throw new AggregateException(error, rollback); }
            throw;
        }
    }
    public void Restore()
    {
        ThreadCheck(); Enabled = false;
        Exception? failure = null;
        for (int i = 0; i < _sites.Length; i++) {
            if (!_owned[i]) continue;
            try {
                var actual = _memory.Read(_sites[i].Rva, 5);
                if (!actual.SequenceEqual(_sites[i].Original)) {
                    Expect(_sites[i].Rva, PatchPlan.Bypass);
                    _memory.Write(_sites[i].Rva, _sites[i].Original);
                    Expect(_sites[i].Rva, _sites[i].Original);
                }
                _owned[i] = false;
            } catch (Exception e) { failure = e; Faulted = true; }
        }
        if (failure != null) throw new InvalidOperationException("Restoration incomplete; restart game. Foreign code was not overwritten.", failure);
    }
}
