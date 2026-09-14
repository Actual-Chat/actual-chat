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
}
