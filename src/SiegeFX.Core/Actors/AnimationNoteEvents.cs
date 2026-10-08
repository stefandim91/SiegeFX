using SiegeFX.Core.Assets;

namespace SiegeFX.Core.Actors;

/// <summary>Crossing detection for authored PRS SFX1..SFX4 notes. PRS note
/// times are normalized within a clip; the renderer's playhead is unbounded
/// for looping chores and clamped for one-shot chores.</summary>
public static class AnimationNoteEvents
{
    private const uint SfxPrefix = 0x00584653; // little-endian "SFX"

    /// <summary>Add the number of each SFX cue crossed between two playhead
    /// positions to <paramref name="counts"/> (slots 0..3 = SFX1..SFX4).
    /// The caller owns and clears the span.</summary>
    public static void AccumulateSfxEvents(PrsAnimation clip, double fromSeconds,
        double toSeconds, bool looping, Span<int> counts)
    {
        if (counts.Length < 4)
            throw new ArgumentException("Four SFX counters are required.", nameof(counts));
        if (clip.AnimLength <= 0f || toSeconds <= fromSeconds) return;
        double length = clip.AnimLength;
        foreach (var note in clip.Notes)
        {
            if ((note.Token & 0x00FFFFFF) != SfxPrefix) continue;
            int ordinal = (int)(note.Token >> 24) - '1';
            if ((uint)ordinal >= 4u) continue;
            double at = Math.Clamp(note.Time, 0f, 1f) * length;
            int crossed;
            if (looping)
            {
                crossed = checked((int)(Math.Floor((toSeconds - at) / length)
                    - Math.Floor((fromSeconds - at) / length)));
            }
            else
            {
                crossed = fromSeconds < at && toSeconds >= at ? 1 : 0;
            }
            // A note at frame zero belongs to the newly selected clip as
            // well as to later loop starts. A paused frame (to == from) was
            // already returned above, so this cannot repeat without progress.
            if (at == 0d && fromSeconds == 0d) crossed++;
            counts[ordinal] += crossed;
        }
    }
}
