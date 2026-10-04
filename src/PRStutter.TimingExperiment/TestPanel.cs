using System;
using System.Diagnostics;
using PRStutter.GridExperiment;
using UnityEngine;

namespace PRStutter.TimingExperiment;

internal static class TestPanel
{
    private sealed class Feature : ITestFeature
    {
        public string Name { get; }
        private readonly Func<bool> _active;
        private readonly Func<string> _reason;
        private readonly Action<long> _start;
        private readonly Action<string> _stop;
        public Feature(string name, Func<bool> active, Func<string> reason, Action<long> start, Action<string> stop)
        { Name = name; _active = active; _reason = reason; _start = start; _stop = stop; }
        public bool Active => _active();
        public string StopReason => _reason();
        public void Start(long deadline) => _start(deadline);
        public void Stop(string reason) => _stop(reason);
    }
    private static readonly CombinedRun Run = new(
        new Feature("Timing", () => Timing.Active, () => Timing.LastStop, Timing.Start, Timing.Stop),
        new Feature("Pacing", () => ExperimentControls.PacingActive, () => ExperimentControls.PacingStopReason,
            ExperimentControls.StartPacing, ExperimentControls.StopPacing),
        new Feature("Smoothing", () => ExperimentControls.SmoothingActive, () => ExperimentControls.SmoothingStopReason,
            ExperimentControls.StartSmoothing, ExperimentControls.StopSmoothing));
    private static int _suppressedFrame = -1;
    private static GUIStyle? _style;
    private static GUIContent? _content;
    private static long _nextRefresh;
    private static long _lastDraw;
    private static int _lastFlags = -1;
    private static float _height;
    private static bool _uiFailed;
    private static bool _reportedReady, _reportedFailure;
    private static bool _panelToggleRequested;
    private static string _message = "Ready; turn CRT off before starting.";
    public static bool SuppressTimingKey => Run.Active || _suppressedFrame == Time.frameCount || Input.GetKeyDown(KeyCode.F4);

    public static void Tick()
    {
        Poll();
        bool panel = _panelToggleRequested; _panelToggleRequested = false;
        bool f4 = Input.GetKeyDown(KeyCode.F4), f5 = Input.GetKeyDown(KeyCode.F5);
        bool f7 = Input.GetKeyDown(KeyCode.F7), f9 = Input.GetKeyDown(KeyCode.F9), f10 = Input.GetKeyDown(KeyCode.F10);
        bool toggle = f4 || panel;
        bool separate = f5 || f7 || f9 || f10;
        if (toggle || separate)
            Timing.Note($"TEST INPUT frame={Time.frameCount} panel={panel} F4={f4} F5={f5} F7={f7} F9={f9} F10={f10}; combinedBefore={Run.Active}.");
        if (Run.Active && (toggle || separate)) { Stop("manual; all three off"); return; }
        if (!toggle) return;
        _suppressedFrame = Time.frameCount;
        if (_uiFailed) throw new InvalidOperationException("Status panel unavailable; restart before a combined test.");
        if (_lastDraw == 0 || Stopwatch.GetTimestamp() - _lastDraw > Stopwatch.Frequency)
            throw new InvalidOperationException("Wait for the status panel to appear before pressing F4.");
        if (!ExperimentControls.Ready) throw new InvalidOperationException("Grid plugin is not ready.");
        ExperimentControls.SetCoordinated(true);
        try {
            if (separate) { Stop("Press F4 alone to start all three"); return; }
            ExperimentControls.StopPixelCapture();
            Run.Start(Stopwatch.GetTimestamp(), Stopwatch.Frequency * 15);
            _message = "Button or F4 stops and saves the capture.";
            Timing.Note("COMBINED ON (15s), F4 stops. Timing + display pacing + 8x smoothing share one deadline. F6 readback disabled; a component safety stop stops all three.");
        } finally { if (!Run.Active) ExperimentControls.SetCoordinated(false); }
    }
    public static void Poll()
    {
        if (!Run.Active) return;
        try { Run.Poll(Stopwatch.GetTimestamp()); }
        finally {
            if (!Run.Active) {
                _suppressedFrame = Time.frameCount;
                ExperimentControls.SetCoordinated(false);
                _message = Run.LastStop;
                Timing.Note("COMBINED OFF: " + _message);
            }
        }
    }
    public static void Stop(string reason)
    {
        bool wasActive = Run.Active;
        _suppressedFrame = Time.frameCount;
        try { Run.Stop(reason); }
        finally {
            ExperimentControls.SetCoordinated(false);
            _message = Run.LastStop;
            if (wasActive) Timing.Note("COMBINED OFF: " + _message);
        }
    }
    public static void FocusLost()
    {
        if (Run.Active) Stop("focus lost; all three off"); else Timing.Stop("focus_lost");
    }
    public static void Fault(Exception e)
    {
        Timing.Note("TEST CONTROL FAULT: " + e);
        try { Stop("error: " + e.Message); }
        catch (Exception cleanup) { Timing.Note("TEST CONTROL CLEANUP FAILED: " + cleanup); }
    }
    private static string State(bool active, double remaining) => active ? $"ON  ({Math.Ceiling(remaining):0}s)" : "OFF";
    public static void Draw()
    {
        if (_uiFailed) return;
        try {
            // Button clicks only queue a command for Update. Submit controls in
            // identical order on every GUI event so mouse/control IDs stay stable.
            if (_style == null) {
                _style = new GUIStyle { alignment = TextAnchor.UpperLeft, wordWrap = true, padding = new RectOffset(12,12,10,10) };
                _style.normal.textColor = Color.white;
            }
            int font = Math.Clamp(Screen.height / 65, 16, 22);
            if (_style.fontSize != font) { _style.fontSize = font; _nextRefresh = 0; }
            int flags = (Timing.Active ? 1 : 0) | (ExperimentControls.PacingActive ? 2 : 0) |
                (ExperimentControls.SmoothingActive ? 4 : 0) | (Run.Active ? 8 : 0) | (Timing.Saving ? 16 : 0);
            long now = Stopwatch.GetTimestamp();
            float width = Math.Min(530, Screen.width - 24);
            if (_content == null || flags != _lastFlags || now >= _nextRefresh) {
                bool all = Timing.Active && ExperimentControls.PacingActive && ExperimentControls.SmoothingActive;
                string header = Run.Active && all ? $"COMBINED TEST ON - {Math.Ceiling(Math.Max(0, (Run.Deadline-now)/(double)Stopwatch.Frequency)):0}s"
                    : Run.Active ? "TEST STOPPING - a feature stopped" : (flags & 7) != 0 ? "SEPARATE CONTROLS ACTIVE" : "TEST OFF - button or F4 to start";
                string smoothing = State(ExperimentControls.SmoothingActive, ExperimentControls.SmoothingRemaining);
                if (ExperimentControls.SmoothingActive) smoothing += ExperimentControls.SmoothingMode.EndsWith("8x") ? "  8x" : "  4x";
                string text = header + "\nTiming: " + State(Timing.Active, Timing.Remaining) + $"  |  Corrections: {Timing.Carried}" +
                    "\nPacing: " + State(ExperimentControls.PacingActive, ExperimentControls.PacingRemaining) +
                    "\nSmoothing: " + smoothing + "\nF4: all on/off   |   CRT must be OFF\n" +
                    (Timing.Saving ? "Saving timing capture..." : _message);
                _content ??= new GUIContent();
                _content.text = text;
                _height = _style.CalcHeight(_content, width);
                _lastFlags = flags; _nextRefresh = now + Stopwatch.Frequency / 10;
            }
            var rect = new Rect(12, 12, width, _height);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(rect, _content, _style);
            var button = new Rect(12, 16 + _height, width, font + 24);
            if (GUI.Button(button, "")) _panelToggleRequested = true;
            GUI.Label(button, Run.Active ? "STOP ALL THREE  (F4)" : "START ALL THREE  (F4)", _style);
            if (Event.current.type == EventType.Repaint) _lastDraw = now;
        } catch (Exception e) { _uiFailed = true; _message = "Status panel failed: " + e.Message; }
    }
    public static void CheckPanel()
    {
        if (_lastDraw != 0 && !_reportedReady) { _reportedReady = true; Timing.Note("STATUS PANEL READY; first draw completed."); }
        if (_uiFailed && !_reportedFailure) { _reportedFailure = true; Timing.Note("STATUS PANEL FAILED: " + _message); }
        if (Run.Active && (_uiFailed || Stopwatch.GetTimestamp() - _lastDraw > Stopwatch.Frequency))
            Stop("status panel unavailable; test stopped");
    }
}
