using System;
using System.Diagnostics;
using PRStutter.GridExperiment;
using UnityEngine;

namespace PRStutter.TimingExperiment;

internal static class TestPanel
{
    private static GUIStyle? _style;
    private static GUIContent? _content;
    private static long _nextRefresh;
    private static long _lastDraw;
    private static int _lastFlags = -1;
    private static float _height;
    private static bool _uiFailed;
    private static bool _reportedReady, _reportedFailure;
    private static bool _panelToggleRequested;
    private static string _message = PRStutter.GridExperiment.GameProfile.HasMidpointClamp
        ? "FFIV preview: native midpoint timing remains unchanged."
        : "FFVI: automatic field corrections; unsupported components suspend.";
    public static bool SuppressTimingKey => true;
    public static void Tick()
    {
        bool toggle = _panelToggleRequested || Input.GetKeyDown(KeyCode.F9);
        _panelToggleRequested = false;
        if (toggle) AutomaticRuntime.Toggle();
    }
    public static void Poll() { }
    public static void Stop(string reason) => AutomaticRuntime.Shutdown(reason);
    public static void FocusLost() => AutomaticRuntime.Suspend("focus lost");
    public static void Fault(Exception e)
    {
        Timing.Note("AUTOMATIC CONTROL FAULT: " + e);
        AutomaticRuntime.Suspend("controller fault: " + e.Message);
    }
    private static string State(bool active, string reason) => active ? "ON" : "SUSPENDED: " + reason;
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
                (ExperimentControls.SmoothingActive ? 4 : 0) | (AutomaticRuntime.Enabled ? 8 : 0) | (Timing.Saving ? 16 : 0);
            long now = Stopwatch.GetTimestamp();
            float width = Math.Min(530, Screen.width - 24);
            if (_content == null || flags != _lastFlags || now >= _nextRefresh) {
                var status = CorrectionStatus.Current;
                string text = (AutomaticRuntime.Enabled ? "AUTOMATIC CORRECTIONS ENABLED" : "CORRECTIONS DISABLED") +
                    "\nTiming: " + State(Timing.Active, status.TimingReason) + $"  |  Corrections: {Timing.Carried}" +
                    "\nPacing: " + State(ExperimentControls.PacingActive, status.PacingReason) +
                    "\nSmoothing: " + State(ExperimentControls.SmoothingActive, status.SmoothingReason) +
                    "\nF9: enable/disable   |   Smoothing requires CRT OFF\n" +
                    (Timing.Saving ? "Saving optional timing capture..." : _message);
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
            GUI.Label(button, AutomaticRuntime.Enabled ? "DISABLE CORRECTIONS  (F9)" : "ENABLE CORRECTIONS  (F9)", _style);
            if (Event.current.type == EventType.Repaint) _lastDraw = now;
        } catch (Exception e) { _uiFailed = true; _message = "Status panel failed: " + e.Message; }
    }
    public static void CheckPanel()
    {
        if (_lastDraw != 0 && !_reportedReady) { _reportedReady = true; Timing.Note("STATUS PANEL READY; first draw completed."); }
        if (_uiFailed && !_reportedFailure) { _reportedFailure = true; Timing.Note("STATUS PANEL FAILED: " + _message); }
        // Panel failure must not disable corrections; F9 remains available in Update.
    }
}
