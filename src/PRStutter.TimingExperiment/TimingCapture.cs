using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BepInEx;

namespace PRStutter.TimingExperiment;

// Legacy bounded CSV capture; correction ownership never depends on this recorder.
internal static class TimingCapture
{
    public static bool Enabled = true;
    private static MovementObservation[]? _rows;
    private static IDisposable? _subscription;
    private static int _count, _dropped;
    private static Task? _save;
    public static bool Saving => _save is { IsCompleted: false };
    public static void Begin()
    {
        if (!Enabled || Saving) return;
        try {
            _rows = new MovementObservation[16384]; _count = _dropped = 0;
            _subscription = CorrectionEvents.Movement.Subscribe(row => {
                if (_rows == null) return;
                if (_count < _rows.Length) _rows[_count++] = row; else _dropped++;
            });
        } catch (Exception e) { _rows = null; Timing.Note("Timing capture unavailable: " + e.Message); }
    }
    public static void End(string reason)
    {
        _subscription?.Dispose(); _subscription = null;
        var rows = _rows; int count = _count, dropped = _dropped; _rows = null;
        if (rows == null) return;
        try { _save = Task.Run(() => Save(rows, count, dropped, reason)); }
        catch (Exception e) { Timing.Note("Timing capture unavailable: " + e.Message); }
    }
    private static void Save(MovementObservation[] rows, int count, int dropped, string reason)
    {
        try {
            string directory = Path.Combine(Paths.BepInExRootPath, "diagnostics/PRStutter/timing");
            Directory.CreateDirectory(directory);
            string stem = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8]);
            var text = new StringBuilder("frame,qpc,delta_time,input_frame,axis_x,axis_y,before_timer,before_duration,before_start_x,before_start_y,before_dest_x,before_dest_y,before_x,before_y,after_timer,after_duration,after_x,after_y,final_timer,final_duration,final_start_x,final_start_y,final_dest_x,final_dest_y,final_x,final_y,remainder,applied,action,postfix_ticks,foot_busy_at_completion,foot_finished_frame,foot_allows_next,prior_camera_frame,arrival_task_action,arrival_iterator_state,arrival_queued_count,arrival_running_count\n");
            for (int i = 0; i < count; i++) {
                var r = rows[i]; var s = r.Sample; var b = s.Before; var a = r.After; var n = r.Final;
                object[] fields = { s.Frame, s.Qpc, s.Delta, s.InputFrame, s.AxisX, s.AxisY, b.Timer, b.Duration, b.Sx, b.Sy, b.Dx, b.Dy, b.X, b.Y, a.Timer, a.Duration, a.X, a.Y, n.Timer, n.Duration, n.Sx, n.Sy, n.Dx, n.Dy, n.X, n.Y, r.Remainder, r.Applied, r.Action, r.Ticks, r.FootBusy, r.FootFrame, r.FootAllowsNext, r.CameraFrame, r.ArrivalAction, r.ArrivalState, r.RequestCount, r.RunningCount };
                for (int j = 0; j < fields.Length; j++) { if (j > 0) text.Append(','); text.Append(Convert.ToString(fields[j], CultureInfo.InvariantCulture)); }
                text.Append('\n');
            }
            File.WriteAllText(stem + ".txt", $"Version: 0.6.1\nQPC frequency: {Stopwatch.Frequency}\nStop: {reason}\nDropped rows: {dropped}\nNo GPU readback. Recording capacity does not stop corrections.\n");
            File.WriteAllText(stem + ".csv", text.ToString());
            Timing.Note("TIMING SAVED " + stem + ".csv");
        } catch (Exception e) { Timing.Note("TIMING SAVE FAILED " + e.Message); }
    }
    public static void Wait() { try { _save?.Wait(3000); } catch { } }
}
