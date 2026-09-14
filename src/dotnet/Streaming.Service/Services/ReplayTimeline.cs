namespace ActualChat.Streaming.Services;

// A dubbed entry's spoken length rarely matches the source's, so the replay timeline is built
// from the dub durations rather than the source entries' own BeginsAt/EndsAt.
internal static class ReplayTimeline
{
    public static TimeSpan PlaysAt(TimeSpan timelinePlaysAt, TimeSpan notBefore)
        => timelinePlaysAt > notBefore ? timelinePlaysAt : notBefore;

    public static TimeSpan ScaleSkip(TimeSpan skipTo, TimeSpan entryDuration, TimeSpan dubDuration)
        => entryDuration <= TimeSpan.Zero || skipTo <= TimeSpan.Zero
            ? TimeSpan.Zero
            : skipTo * (dubDuration / entryDuration);
}
