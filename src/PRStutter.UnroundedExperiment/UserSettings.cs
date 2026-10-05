using System;
using System.IO;
using System.Text.Json;

namespace PRStutter.UnroundedExperiment;

// User intent only. Scene suspension, active targets and diagnostic recordings
// are deliberately absent from this file.
internal sealed record UserSettings
{
    public int Version { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public int RenderScale { get; init; } = 4;
    public string MenuKey { get; init; } = "F11";
    public UserSettings Validate()
    {
        if (Version != 1 || RenderScale is not (1 or 4 or 8) || MenuKey is not ("F11" or "F10" or "Insert"))
            throw new InvalidDataException("Unsupported settings version, render scale or menu key.");
        return this;
    }
}

internal sealed class SettingsStore
{
    private readonly string _path;
    private bool _readOnly;
    public UserSettings Current { get; private set; } = new();
    public string Status { get; private set; } = "Settings ready";
    public SettingsStore(string path) => _path = path;
    public void Load()
    {
        try {
            if (File.Exists(_path)) Current = (JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path))
                ?? throw new InvalidDataException("Empty settings")).Validate();
            else Save(Current);
        } catch (Exception e) {
            // Preserve malformed/newer files; do not enable corrections on an
            // ambiguous configuration or overwrite someone's existing settings.
            Current = new UserSettings { Enabled = false }; _readOnly = true;
            Status = "Could not load settings; original preserved: " + e.Message;
        }
    }
    public bool Save(UserSettings value)
    {
        value.Validate(); Current = value;
        if (_readOnly) { Status = "Session only: existing settings could not be read; original preserved."; return false; }
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(true);
            }
            File.Move(temporary, _path, true);
            Status = "Settings saved automatically"; return true;
        } catch (Exception e) { Status = "Session only: settings could not be saved: " + e.Message; return false; }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }
}
