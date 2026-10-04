using System.Collections.Generic;

namespace PRStutter.GridExperiment;

// Named topologies observed in the inspected games, not a permissive count check.
public static class FieldLayoutPolicy
{
    public static int RenderCameraCount(string game) => game == "FFIV" ? 1 : game == "FFVI" ? 3 : 0;
    public static bool Accepts(string game, IEnumerable<string> cameraNames, int textureCount)
    {
        var names = new HashSet<string>();
        foreach (string name in cameraNames) if (!names.Add(name)) return false;
        if (!names.Contains("CameraFieldMain") || !names.Contains("CameraTileMap")) return false;
        if (game == "FFIV") return names.Count == 2 && textureCount == 1;
        return game == "FFVI" && names.Count == 4 && textureCount == 3 &&
            names.Contains("CameraUpperTransparentRT") && names.Contains("CameraCeilTransparentRT");
    }
}
