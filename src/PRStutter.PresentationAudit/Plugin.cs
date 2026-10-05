using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Last.Map;
using UnityEngine;

namespace PRStutter.PresentationAudit;

[BepInPlugin("local.prstutter.presentationaudit", "PR Stutter Presentation Audit", "0.3.0")]
public sealed class Plugin : BasePlugin
{
    private Harmony? _harmony;
    private Driver? _driver;
    public override void Load()
    {
        if (!MatchesBuild()) { Log.LogError("Presentation audit: unsupported binary; no hooks installed."); return; }
        try {
            _harmony = new Harmony("local.prstutter.presentationaudit");
            var original = AccessTools.DeclaredMethod(typeof(FieldController), "UpdateVisualInstancePosition", Type.EmptyTypes)
                ?? throw new MissingMethodException("Field visual boundary not found");
            _harmony.Patch(original, postfix: new HarmonyMethod(typeof(Observer).GetMethod(nameof(Observer.AfterVisuals), BindingFlags.Public | BindingFlags.Static)!));
            _driver = AddComponent<Driver>();
            Log.LogInfo("Read-only presentation audit ready. Ctrl+F11 starts/stops a 60-second capture; stock movement can stay enabled. 20 Hz spatial + 4 Hz lifecycle evidence; no correction dependency or game setters.");
        } catch { _harmony?.UnpatchSelf(); throw; }
    }
    public override bool Unload()
    {
        Observer.Stop("unload"); _harmony?.UnpatchSelf(); Observer.Wait();
        if (_driver != null) UnityEngine.Object.Destroy(_driver);
        return true;
    }
#if PR_FFIV
    internal const string Game = "FFIV", Roman = "IV";
    private const string AssemblyHash = "bb2f4c9db44c8ee9b065696aeafb77561a1709492130f045433e6d215abc42ed";
    private const string MetadataHash = "37400ca079eddb18cda06450c02c7bd8eb2c057175502e75e2e2c8bc09b31f41";
#else
    internal const string Game = "FFVI", Roman = "VI";
    private const string AssemblyHash = "0029a22ed933aa3b6ea3b1290060502267f514d61ba6e6619308d844557f2ffd";
    private const string MetadataHash = "f50d9d1ff84f8033b8acbdc6845ab3a0f2793dd253c36a4a7d212303980844dd";
#endif
    private static bool MatchesBuild()
    {
        static bool Match(string path, string hash) {
            using var stream = File.OpenRead(Path.Combine(Paths.GameRootPath, path));
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase);
        }
        return Match("GameAssembly.dll", AssemblyHash) && Match($"FINAL FANTASY {Roman}_Data/il2cpp_data/Metadata/global-metadata.dat", MetadataHash);
    }
}
public static class AuditControl
{
    private static bool _requested;
    public static string Status => Observer.Status;
    public static void RequestToggle() => _requested = true;
    internal static bool TakeRequest() { bool value = _requested; _requested = false; return value; }
}
public sealed class Driver : MonoBehaviour
{
    public Driver(IntPtr pointer) : base(pointer) { }
    public void Update()
    {
        try {
            bool key = Input.GetKeyDown(KeyCode.F11) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt) && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift);
            if (AuditControl.TakeRequest() || key) Observer.Toggle();
            if (Observer.Expired) Observer.Stop("duration");
            Observer.ObserveLifecycle();
        } catch (Exception e) { Observer.Fault(e); }
    }
    public void OnGUI() { if (!Observer.Recording) return; try { GUI.Label(new Rect(12, Screen.height - 90, Math.Max(200, Screen.width - 24), 50), Observer.Status); } catch { } }
    public void OnApplicationQuit() { Observer.Stop("quit"); Observer.Wait(); }
}
