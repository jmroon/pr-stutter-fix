using System;
using System.Collections.Generic;

namespace PRStutter.PlaythroughDiagnostics;

// Pure bounded storage and trigger policy; no Unity or correction dependencies.
public sealed class RecentBuffer<T>
{
    private readonly T[] _items;
    private readonly long[] _ticks;
    private int _head, _count;
    public long Overwritten { get; private set; }
    public RecentBuffer(int capacity) { if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); _items = new T[capacity]; _ticks = new long[capacity]; }
    public void Add(long qpc, T item)
    {
        _items[_head] = item; _ticks[_head] = qpc; _head = (_head + 1) % _items.Length;
        if (_count < _items.Length) _count++; else Overwritten++;
    }
    public T[] Snapshot(long since)
    {
        var result = new List<T>(_count);
        for (int i = 0; i < _count; i++) {
            int slot = (_head - _count + i + _items.Length) % _items.Length;
            if (_ticks[slot] >= since) result.Add(_items[slot]);
        }
        return result.ToArray();
    }
}

public sealed class IncidentGate
{
    private readonly HashSet<string> _seen = new();
    private readonly long _cooldown, _post;
    private long _next;
    public string? Reason { get; private set; }
    public long TriggerQpc { get; private set; }
    public long DueQpc { get; private set; }
    public int Suppressed { get; private set; }
    public IncidentGate(long cooldown, long post) { _cooldown = cooldown; _post = post; }
    public bool Trigger(string reason, long now, bool manual = false)
    {
        if (Reason != null || (!manual && (now < _next || _seen.Contains(reason) || _seen.Count >= 128))) { Suppressed++; return false; }
        Reason = reason; TriggerQpc = now; DueQpc = now + _post; _next = now + _cooldown;
        if (!manual) _seen.Add(reason);
        return true;
    }
    public void Complete() => Reason = null;
}

public readonly record struct MotionSample(int Frame, long Qpc, float Delta, int CameraId, int EntityId,
    float X, float Y, float CameraX, float CameraY, float ScreenX, float ScreenY,
    float Sx, float Sy, float Dx, float Dy, float Timer, float Duration,
    float ProjectedZ, float CameraSize, int PixelWidth, int PixelHeight);
public readonly record struct FrameCost(int Frame, long Qpc, long ObserverTicks, long WallIntervalTicks,
    bool Timing, bool Pacing, bool Smoothing);
public sealed record CameraInfo(int Id, string Name, string Scene, int TargetId, int Width, int Height, bool Orthographic, float Size, float Depth, int Mask);
public sealed record EntityInfo(int Id, string Name, string Scene, bool Player);
