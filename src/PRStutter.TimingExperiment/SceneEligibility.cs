namespace PRStutter.TimingExperiment;

internal readonly record struct FieldContextSnapshot(string Identity, string TimingIdentity, string Kind,
    bool Precision, bool Pacing, bool Manual, string Reason);

internal static class SceneEligibility
{
    // Native state names are shared by inspected FFIV/FFVI enums. No map IDs.
    public static bool FieldState(string state) => state is "Player" or "Event" or "Message" or "Gimmick";
    public static bool ExpectedTimingStop(string reason) => reason is "control_changed" or "control_changed_after_callbacks" or
        "timeout_or_control_changed" or "focus lost" or "context changed" or "context unavailable";
    public static bool ExpectedPacingStop(string reason) => reason is "context_changed" or "focus_lost" or
        "context changed" or "context unavailable";
}
