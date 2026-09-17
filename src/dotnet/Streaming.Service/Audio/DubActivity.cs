namespace ActualChat.Streaming;

/// <summary>
/// Whether any dub of one author in one language is speaking right now: the next utterance's mix
/// starts ducked while the previous dub is still draining. Shared by every mix of that author.
/// </summary>
public sealed class DubActivity
{
    private long _speakingUntilTicks;

    public Moment SpeakingUntil => new(Volatile.Read(ref _speakingUntilTicks));

    public bool IsSpeaking(Moment now)
        => SpeakingUntil > now;

    public void MarkSpeaking(Moment until)
    {
        // Monotonic: an earlier deadline never shortens a later one
        var ticks = until.EpochOffsetTicks;
        while (true) {
            var current = Volatile.Read(ref _speakingUntilTicks);
            if (current >= ticks)
                return;
            if (Interlocked.CompareExchange(ref _speakingUntilTicks, ticks, current) == current)
                return;
        }
    }
}
