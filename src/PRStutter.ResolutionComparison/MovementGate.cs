namespace PRStutter.UnroundedExperiment;

// Value identity avoids building/logging context strings every render frame.
internal readonly record struct RenderContext(string Identity, int Generation, long Field, long Map,
    int Scale, int Revision, int Width, int Height, long Material, float MainBias, float OverlayBias);

internal readonly record struct MovementGate(bool Enabled, bool Precision, bool Timing, bool Pacing,
    bool Faulted, string Kind, string Identity, int Generation, long Field, long Map, int ContextFrame)
{
    public bool Eligible(int frame) => Enabled && Precision && Timing && Pacing && !Faulted &&
        Kind == "field-manual" && Field != 0 && Map != 0 && frame - ContextFrame is >= 0 and <= 1;
    public bool SameContext(MovementGate other) => Identity == other.Identity && Generation == other.Generation &&
        Field == other.Field && Map == other.Map;
}
