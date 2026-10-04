using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace PRStutter.Diagnostics;

[BepInPlugin(Id, "PR Stutter Diagnostics", Version)]
public sealed class Plugin : BasePlugin
{
    public const string Id = "local.prstutter.diagnostics";
    public const string Version = "0.3.1";
    public const string SupportedGameHash = "0029a22ed933aa3b6ea3b1290060502267f514d61ba6e6619308d844557f2ffd";
    private const string SupportedMetadataHash = "f50d9d1ff84f8033b8acbdc6845ab3a0f2793dd253c36a4a7d212303980844dd";
    private CaptureDriver? _driver;

    public override void Load()
    {
        if (!Matches(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"), SupportedGameHash) ||
            !Matches(Path.Combine(Paths.GameRootPath, "FINAL FANTASY VI_Data", "il2cpp_data", "Metadata", "global-metadata.dat"), SupportedMetadataHash))
        {
            Log.LogError("Game build does not match the inspected FFVI build. Diagnostics will remain disabled.");
            return;
        }
        var duration = Config.Bind("Capture", "DurationSeconds", 15,
            new ConfigDescription("F8 starts a bounded, logging-only capture. F8 again stops it early.", new AcceptableValueRange<int>(1, 60)));
        string output = Path.Combine(Paths.BepInExRootPath, "diagnostics", "PRStutter");
        Recorder.Initialize(Log, output, duration.Value, Application.unityVersion);
        try
        {
            _driver = AddComponent<CaptureDriver>();
            Log.LogInfo($"Polling diagnostics ready. F8 records {duration.Value}s to {output}. No game-method patches or camera callbacks are installed. Live visual verification is required.");
        }
        catch (Exception error)
        {
            Unload();
            Log.LogError($"Diagnostic setup failed; driver removed. {error}");
        }
    }

    public override bool Unload()
    {
        Recorder.Shutdown();
        if (_driver != null) { UnityEngine.Object.Destroy(_driver); _driver = null; }
        return true;
    }

    private static bool Matches(string path, string expected)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CaptureDriver : MonoBehaviour
{
    public CaptureDriver(IntPtr pointer) : base(pointer) { }
    public void Update() { try { Recorder.Tick(); } catch (Exception error) { Recorder.Fault(error); } }
    public void LateUpdate()
    {
        if (!Recorder.Active) return;
        try { Recorder.Late(); } catch (Exception error) { Recorder.Fault(error); }
    }
    public void OnApplicationQuit() => Recorder.Shutdown();
}
