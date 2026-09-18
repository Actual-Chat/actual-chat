namespace ActualChat.Streaming.Services;

// A dubbed entry's spoken length rarely matches the source's, so the replay timeline is built
// from the dub durations rather than the source entries' own BeginsAt/EndsAt.
internal static class ReplayTimeline
{
    // stretchTimeline: false for an undubbed replay, where concurrent speakers must stay concurrent
    // rather than being serialized by a dub-reservation timeline that never applied to them
    public static TimeSpan PlaysAt(TimeSpan timelinePlaysAt, TimeSpan notBefore, bool stretchTimeline = true)
        => stretchTimeline && timelinePlaysAt < notBefore ? notBefore : timelinePlaysAt;

    public static TimeSpan ScaleSkip(TimeSpan skipTo, TimeSpan entryDuration, TimeSpan dubDuration)
        => entryDuration <= TimeSpan.Zero || skipTo <= TimeSpan.Zero
            ? TimeSpan.Zero
            : skipTo * (dubDuration / entryDuration);

    public static TimeSpan SkippedGap(TimeSpan gap, TimeSpan maxGap)
        => (gap - maxGap).Positive();

    // The streamed offset (counted from skipTo) past which only the VAD's trailing silence is left
    public static TimeSpan TailCutoff(TimeSpan speechDuration, TimeSpan skipTo, TimeSpan tailMargin)
        => speechDuration + tailMargin - skipTo;

    public static TimeSpan Deadline(TimeSpan playsAt, TimeSpan frameOffset, double speed)
        => playsAt + frameOffset / speed;

    public static bool MustKeepFrame(int frameIndex, double speed)
        // Keeps 1/speed of the frames, spread evenly: a frame survives when it moves the running
        // count of kept frames to the next integer
        => Math.Floor((frameIndex + 1) / speed) > Math.Floor(frameIndex / speed);
}
