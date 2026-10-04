using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PRStutter.Diagnostics;

public enum Phase { MovementResult, CameraAfter, LateUpdate, PreCull }

public readonly record struct Point3(float X, float Y, float Z)
{
    public static Point3 Missing => new(float.NaN, float.NaN, float.NaN);
}

// Plain managed data: safe to serialize off the Unity thread.
public struct CaptureSample
{
    public Phase Phase;
    public int Frame, EntityId, ControllerId, CameraId, PlayerMoveState;
    public long Qpc, ObserverTicks;
    public float DeltaTime, UnscaledDeltaTime, TimeScale, Timer, Duration;
    public Point3 Start, Destination, Candidate, Result, Local, World, VisualWorld, SpriteWorld;
    public Point3 CameraWorld, CameraTarget, ScrollOffset, ScreenPoint, SpriteScreenPoint;
    public int ScreenWidth, ScreenHeight, PixelWidth, PixelHeight, TargetWidth, TargetHeight;
    public float OrthographicSize;
}

public sealed class CaptureBuffer
{
    public CaptureSample[] Samples { get; }
    public int Count { get; private set; }
    public int Dropped { get; private set; }
    public CaptureBuffer(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Samples = new CaptureSample[capacity];
    }
    public bool Add(in CaptureSample sample)
    {
        if (Count == Samples.Length) { Dropped++; return false; }
        Samples[Count++] = sample;
        return true;
    }
}

public sealed record CameraDescription(int Id, string Name, int CullingMask, string TargetName, int FilterMode);
public sealed record CaptureMetadata(
    int SchemaVersion, string PluginVersion, string GameSha256, string UnityVersion,
    string StartedUtc, long QpcFrequency, long StartQpc, long EndQpc,
    string StopReason, int Rows, int DroppedRows, int MovementRows, int CameraRows, int PreCullRows,
    IReadOnlyCollection<CameraDescription> Cameras, string ObservationMode = "NativeHooks");

public static class CaptureFiles
{
    public const string Header = "phase,frame,entity_id,controller_id,camera_id,player_move_state,qpc,observer_ticks,delta_time,unscaled_delta_time,time_scale,move_timer,move_duration," +
        "start_x,start_y,start_z,dest_x,dest_y,dest_z,candidate_x,candidate_y,candidate_z,result_x,result_y,result_z," +
        "local_x,local_y,local_z,world_x,world_y,world_z,visual_world_x,visual_world_y,visual_world_z,sprite_world_x,sprite_world_y,sprite_world_z," +
        "camera_world_x,camera_world_y,camera_world_z,camera_target_x,camera_target_y,camera_target_z," +
        "scroll_x,scroll_y,scroll_z,screen_point_x,screen_point_y,screen_point_z,sprite_screen_x,sprite_screen_y,sprite_screen_z," +
        "screen_width,screen_height,pixel_width,pixel_height,target_width,target_height,orthographic_size";

    public static void Write(string stem, CaptureBuffer buffer, CaptureMetadata metadata)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stem)!);
        string csv = stem + ".csv";
        using (var writer = new StreamWriter(csv + ".partial", false, new UTF8Encoding(false), 65536))
        {
            writer.WriteLine(Header);
            var row = new StringBuilder(1024);
            for (int i = 0; i < buffer.Count; i++)
            {
                ref readonly CaptureSample s = ref buffer.Samples[i];
                row.Clear();
                row.Append(s.Phase);
                Add(row, s.Frame); Add(row, s.EntityId); Add(row, s.ControllerId); Add(row, s.CameraId);
                Add(row, s.PlayerMoveState);
                Add(row, s.Qpc); Add(row, s.ObserverTicks);
                Add(row, s.DeltaTime); Add(row, s.UnscaledDeltaTime); Add(row, s.TimeScale);
                Add(row, s.Timer); Add(row, s.Duration);
                Add(row, s.Start); Add(row, s.Destination); Add(row, s.Candidate); Add(row, s.Result);
                Add(row, s.Local); Add(row, s.World); Add(row, s.VisualWorld); Add(row, s.SpriteWorld);
                Add(row, s.CameraWorld); Add(row, s.CameraTarget); Add(row, s.ScrollOffset); Add(row, s.ScreenPoint);
                Add(row, s.SpriteScreenPoint);
                Add(row, s.ScreenWidth); Add(row, s.ScreenHeight); Add(row, s.PixelWidth); Add(row, s.PixelHeight);
                Add(row, s.TargetWidth); Add(row, s.TargetHeight); Add(row, s.OrthographicSize);
                writer.WriteLine(row);
            }
        }
        File.WriteAllText(stem + ".json.partial", JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        // Commit metadata first, CSV last: a visible completed CSV always has its metadata.
        File.Move(stem + ".json.partial", stem + ".json");
        File.Move(csv + ".partial", csv);
    }

    private static void Add(StringBuilder row, long value) => row.Append(',').Append(value.ToString(CultureInfo.InvariantCulture));
    private static void Add(StringBuilder row, float value) => row.Append(',').Append(value.ToString("R", CultureInfo.InvariantCulture));
    private static void Add(StringBuilder row, Point3 value) { Add(row, value.X); Add(row, value.Y); Add(row, value.Z); }
}
