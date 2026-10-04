using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using Last.Entity.Field;

namespace PRStutter.GridExperiment;

// One compiled profile per inspected game. Bindings and binary gates must agree.
public static class GameProfile
{
#if PR_FFIV
    public const string Id = "FFIV";
    public const string AssemblyHash = "bb2f4c9db44c8ee9b065696aeafb77561a1709492130f045433e6d215abc42ed";
    public const string MetadataHash = "37400ca079eddb18cda06450c02c7bd8eb2c057175502e75e2e2c8bc09b31f41";
    public const string DataDirectory = "FINAL FANTASY IV_Data";
    public static bool HasMidpointClamp => true;
    public static bool PathQueueEmpty(FieldPlayer player) => player.movementPositionList != null && player.movementPositionList.Count == 0;
    // FFIV has no IsRiging property. All vehicles have a non-Walk moveState;
    // reject takeoff/landing and low-flying transitions as well.
    public static bool TransportActive(FieldPlayer player) =>
        (int)player.moveState != 0 || player.IsDuringTakeoffAndLanding || player.lowFlying;
#else
    public const string Id = "FFVI";
    public const string AssemblyHash = "0029a22ed933aa3b6ea3b1290060502267f514d61ba6e6619308d844557f2ffd";
    public const string MetadataHash = "f50d9d1ff84f8033b8acbdc6845ab3a0f2793dd253c36a4a7d212303980844dd";
    public const string DataDirectory = "FINAL FANTASY VI_Data";
    public static bool HasMidpointClamp => false;
    public static bool PathQueueEmpty(FieldPlayer player) => player.MovementPositionList != null && player.MovementPositionList.Count == 0;
    public static bool TransportActive(FieldPlayer player) => player.IsRiging;
#endif
    public static bool MatchesInstalledBuild(string expectedProfile)
    {
        return expectedProfile == Id && Matches("GameAssembly.dll", AssemblyHash) &&
            Matches(Path.Combine(DataDirectory, "il2cpp_data/Metadata/global-metadata.dat"), MetadataHash);
    }
    private static bool Matches(string relative, string expected)
    {
        string path = Path.Combine(Paths.GameRootPath, relative);
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
