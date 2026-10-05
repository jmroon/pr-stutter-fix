using System;
using System.IO;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace PRStutter.UnroundedExperiment;

// Read-only settings contract consumed by the optional renderer. Neither the
// renderer nor the independent recorder can change saved user intent.
public static class SettingsStatus
{
    public static bool Enabled => SettingsMenu.Current.Enabled;
    public static int RenderScale => SettingsMenu.Current.RenderScale;
    public static int Revision => SettingsMenu.Revision;
}

internal static class SettingsMenu
{
    private static readonly SettingsStore Store = new(Path.Combine(Paths.ConfigPath, "prstutter.settings.json"));
    private static UserSettings? _pending;
    private static bool _visible, _advanced, _retry, _capture;
    private static GUIStyle? _label, _button;
    private static bool _cursorOwned, _cursorVisible;
    private static CursorLockMode _cursorLock;
    private static Vector2 _scroll;
    private static float _contentHeight = 1000;
    public static UserSettings Current => Store.Current;
    public static int Revision { get; private set; }
    public static void Initialize() => Store.Load();
    public static void Tick()
    {
        bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        if (Application.isFocused && !control && !shift && !alt && Input.GetKeyDown(Current.MenuKey == "F10" ? KeyCode.F10 : Current.MenuKey == "Insert" ? KeyCode.Insert : KeyCode.F11)) SetVisible(!_visible);
        if (_pending != null) {
            var next = _pending; _pending = null;
            bool changed = next.Enabled != Current.Enabled;
            bool renderingChanged = changed || next.RenderScale != Current.RenderScale;
            if (!Store.Save(next)) Experiment.Log?.LogWarning(Store.Status);
            if (renderingChanged) Revision++;
            if (changed) Experiment.RequestEnabled(next.Enabled);
        }
        if (_retry) { _retry = false; Revision++; }
        if (_capture) {
            _capture = false;
            Find("PRStutter.PresentationAudit", "PRStutter.PresentationAudit.AuditControl")?.GetMethod("RequestToggle")?.Invoke(null, null);
        }
    }
    private static Type? Find(string assemblyName, string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == assemblyName) return assembly.GetType(typeName);
        return null;
    }
    public static void SetVisible(bool value)
    {
        _visible = value;
        if (value && !_cursorOwned) {
            _cursorVisible = Cursor.visible; _cursorLock = Cursor.lockState; _cursorOwned = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        } else if (!value && _cursorOwned) {
            // Respect a subsequent game's cursor change rather than replacing it.
            if (Cursor.lockState == CursorLockMode.None) Cursor.lockState = _cursorLock;
            if (Cursor.visible) Cursor.visible = _cursorVisible;
            _cursorOwned = false;
        }
    }
    public static void Draw()
    {
        if (!_visible) return;
        _label ??= new GUIStyle { wordWrap = true, alignment = TextAnchor.UpperLeft };
        _button ??= new GUIStyle { wordWrap = true, alignment = TextAnchor.MiddleCenter };
        int font = Math.Clamp(Screen.height / 55, 14, 22);
        _label.fontSize = _button.fontSize = font;
        _label.normal.textColor = _button.normal.textColor = Color.white;
        float width = Math.Min(650, Screen.width - 24), height = Math.Min(690, Screen.height - 24);
        float x = (Screen.width - width) / 2, y = (Screen.height - height) / 2;
        GUI.Box(new Rect(x, y, width, height), "");
        _scroll = GUI.BeginScrollView(new Rect(x + 4, y + 4, width - 8, height - 8), _scroll,
            new Rect(0, 0, width - 30, _contentHeight));
        x = 0; width -= 30; float top = 12;
        try {
            void Label(string text, float rows = 1) {
                float h = Math.Max((font + 5) * rows, _label!.CalcHeight(new GUIContent(text), width - 28));
                GUI.Label(new Rect(x + 14, top, width - 28, h), text, _label); top += h + 5;
            }
            bool Button(string text) {
                var rect = new Rect(x + 14, top, width - 28, font + 19); top += font + 24;
                bool pressed = GUI.Button(rect, ""); GUI.Label(rect, text, _button); return pressed;
            }
            Label("PR Stutter Fix");
            Label("Pause the game before changing settings. This menu does not pause gameplay.", 2);
            if (Button(Current.Enabled ? "Smooth movement: ON" : "Smooth movement: OFF")) _pending = Current with { Enabled = !Current.Enabled };
            string scale = Current.RenderScale == 1 ? "Native (320 x 180)" : Current.RenderScale == 4 ? "4x (1280 x 720)" : "8x (2560 x 1440)";
            if (Button("Rendering: " + scale + "  >")) _pending = Current with { RenderScale = Current.RenderScale == 1 ? 4 : Current.RenderScale == 4 ? 8 : 1 };
            Label("4x is recommended. 8x costs more GPU memory and work. Higher resolution requires CRT off.", 2);
            var renderer = Find("PRStutter.ResolutionComparison", "PRStutter.UnroundedExperiment.ResolutionStatus");
            string render = renderer?.GetProperty("Status")?.GetValue(null)?.ToString() ?? "Renderer module not installed; native resolution";
            Label("Movement: " + (Experiment.State == "fault" ? "FAULT - restart game" : !Experiment.Running ? "OFF" : Experiment.State == "on" ? "active" : "waiting for supported scene"));
            Label("Rendering: " + render, 2);
            if (Button("Menu key: " + Current.MenuKey + "  >")) _pending = Current with { MenuKey = Current.MenuKey == "F11" ? "F10" : Current.MenuKey == "F10" ? "Insert" : "F11" };
            if (Button(_advanced ? "Hide diagnostics" : "Diagnostics and troubleshooting")) _advanced = !_advanced;
            if (_advanced) {
                var audit = Find("PRStutter.PresentationAudit", "PRStutter.PresentationAudit.AuditControl");
                Label(audit?.GetProperty("Status")?.GetValue(null)?.ToString() ?? "Optional recorder not installed", 2);
                if (audit != null && Button("Start / stop 60-second recording (Ctrl+F11)")) _capture = true;
                if (Button("Retry rendering in this scene")) _retry = true;
            }
            Label(Store.Status, 2);
            if (Button("Close (" + Current.MenuKey + ")")) SetVisible(false);
            _contentHeight = top + 12;
        } finally { GUI.EndScrollView(); }
    }
}
