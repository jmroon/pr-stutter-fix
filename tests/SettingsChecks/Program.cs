using System;
using System.IO;
using PRStutter.UnroundedExperiment;

static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
string directory = Path.Combine(Path.GetTempPath(), "prstutter-settings-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try {
    string path = Path.Combine(directory, "settings.json");
    var store = new SettingsStore(path); store.Load();
    Check(store.Current.Enabled && store.Current.RenderScale == 4 && store.Current.MenuKey == "F11", "First-run defaults");
    var wanted = store.Current with { Enabled = false, RenderScale = 8, MenuKey = "Insert" };
    Check(store.Save(wanted), "Could not save changed preferences");
    var restart = new SettingsStore(path); restart.Load();
    Check(restart.Current == wanted, "Restart did not restore preferences");
    Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Temporary file leaked");
    string bytes = File.ReadAllText(path);
    bool refused = false;
    try { store.Save(wanted with { RenderScale = 2 }); } catch (InvalidDataException) { refused = true; }
    Check(refused && File.ReadAllText(path) == bytes, "Invalid scale overwrote settings");
    foreach (string corrupt in new[] { "{", "null", "{\"Version\":2}", "{\"MenuKey\":\"None\"}", "{\"RenderScale\":3}" }) {
        File.WriteAllText(path, corrupt);
        var broken = new SettingsStore(path); broken.Load();
        Check(!broken.Current.Enabled && File.ReadAllText(path) == corrupt, "Invalid/newer settings silently enabled or overwritten");
        Check(!broken.Save(wanted) && File.ReadAllText(path) == corrupt, "Unreadable settings not preserved");
    }
    File.WriteAllText(path, bytes);
    using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) {
        Check(!store.Save(wanted with { RenderScale = 1 }), "Exclusive file lock did not prevent replacement");
        Check(store.Current.RenderScale == 1 && store.Status.StartsWith("Session only"), "Save failure lost session choice or was hidden");
    }
    Check(File.ReadAllText(path) == bytes, "Failed save damaged previous settings");
    Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Failed save leaked temporary file");
    Check(store.Save(wanted with { RenderScale = 4 }), "Save did not recover after lock release");
    Console.WriteLine("PASS: defaults, restart round trip, validated schema, preserved corrupt/newer files, atomic replacement, locked-file failure/recovery and temporary-file cleanup.");
} finally { Directory.Delete(directory, true); }
