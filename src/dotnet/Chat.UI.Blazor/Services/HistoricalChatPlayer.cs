using ActualChat.MediaPlayback;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class HistoricalChatPlayer : ChatPlayer
{
    // Once enqueued, playback loop continues, so the larger is this duration,
    // the higher is the chance to enqueue the next entry on time.
    private static readonly TimeSpan EnqueueAheadDuration = TimeSpan.FromSeconds(1);

    public HistoricalChatPlayer(IServiceProvider services) : base(services) { }

    protected override async Task PlayInternal(Moment startAt, Playback playback, CancellationToken cancellationToken)
    {
        var cpuClock = Clocks.CpuClock;

        var idRange = await Chats.GetIdRange(Session, ChatId, ChatEntryType.Audio, cancellationToken)
            .ConfigureAwait(false);
        var startEntry = await AudioEntryReader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        if (startEntry == null) {
            Log.LogWarning("Couldn't find start entry");
            return;
        }

        var playbackBlockEnd = cpuClock.Now - TimeSpan.FromDays(1); // Any time in past
        var playbackOffset = playbackBlockEnd - Moment.EpochStart; // now - playTime

        idRange = (startEntry.Id, idRange.End);
        var entries = AudioEntryReader
            .ReadAll(idRange, cancellationToken)
            .Where(e => e.Type == ChatEntryType.Audio);
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (!entry.StreamId.IsEmpty) // Streaming entry
                continue;
            if (entry.EndsAt < startAt)
                // We're normally starting @ (startAt - ChatConstants.MaxEntryDuration),
                // so we need to skip a few entries.
                continue;

            var now = cpuClock.Now;
            var entryBeginsAt = Moment.Max(entry.BeginsAt, startAt);
            var entryEndsAt = entry.ContentEndsAt ?? entry.EndsAt ?? entry.BeginsAt + InfDuration;
            var skipTo = entryBeginsAt - entry.BeginsAt;
            if (playbackBlockEnd < entryBeginsAt + playbackOffset) {
                // There is a gap between the currently playing "block" and the entry.
                // This means we're still playing the "historical" block, and the new entry
                // starts with some gap after it; we're going to nullify this gap here by
                // adjusting realtimeOffset.
                playbackBlockEnd = Moment.Max(now, playbackBlockEnd);
                playbackOffset = playbackBlockEnd - entryBeginsAt;
            }

            var playAt = entryBeginsAt + playbackOffset;
            playbackBlockEnd = Moment.Max(playbackBlockEnd, entryEndsAt + playbackOffset);

            var enqueueDelay = playAt - now - EnqueueAheadDuration;
            if (enqueueDelay > TimeSpan.Zero)
                await cpuClock.Delay(enqueueDelay, cancellationToken).ConfigureAwait(false);
            await EnqueueEntry(playback, playAt, entry, skipTo, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
