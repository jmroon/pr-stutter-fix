using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PRStutter.PlaythroughDiagnostics;

// One in-flight write; never queue unbounded snapshots or block gameplay for disk.
public sealed class CaptureWriter
{
    private readonly string _directory;
    private readonly long _budget;
    private readonly int _maxFiles;
    private Task? _task;
    public int Dropped { get; private set; }
    public string Status { get; private set; } = "ready";
    public bool Busy => _task is { IsCompleted: false };
    public void RecordBusyDrop() => Dropped++;
    public CaptureWriter(string directory, long budget = 512L * 1024 * 1024, int maxFiles = 128)
    { _directory = directory; _budget = budget; _maxFiles = maxFiles; }
    public bool TrySave(object snapshot)
    {
        if (Busy) { Dropped++; return false; }
        try { _task = Task.Run(() => Write(snapshot)); return true; }
        catch (Exception e) { Status = "writer unavailable: " + e.Message; Dropped++; return false; }
    }
    private void Write(object snapshot)
    {
        try {
            Directory.CreateDirectory(_directory);
            var files = new DirectoryInfo(_directory).GetFiles();
            long used = files.Sum(f => f.Length);
            if (files.Length >= _maxFiles || used >= _budget) { Status = "storage budget reached; no files deleted"; return; }
            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions {
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
            int bytes = Encoding.UTF8.GetByteCount(json);
            if (bytes > 32 * 1024 * 1024 || bytes > _budget - used) { Status = "capture exceeds storage budget"; return; }
            string path = Path.Combine(_directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8] + ".json");
            File.WriteAllText(path + ".partial", json, new UTF8Encoding(false));
            File.Move(path + ".partial", path);
            Status = "saved " + Path.GetFileName(path);
        } catch (Exception e) { Status = "save failed: " + e.Message; }
    }
    public void Wait() { try { _task?.Wait(3000); } catch { } }
}
