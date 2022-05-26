using ActualChat.MediaPlayback;
using ActualChat.Messaging;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class HistoricalChatPlayer : ChatPlayer
{
    public HistoricalChatPlayer(Session session, Symbol chatId, IServiceProvider services)
        : base(session, chatId, services)
        => PlayerKind = ChatPlayerKind.Historical;

    protected override async Task Play(Moment startAt, CancellationToken cancellationToken)
    {
        var cpuClock = Clocks.CpuClock;
        var audioEntryReader = Chats.NewEntryReader(Session, ChatId, ChatEntryType.Audio);
        var idRange = await Chats.GetIdRange(Session, ChatId, ChatEntryType.Audio, cancellationToken)
            .ConfigureAwait(false);
        var startEntry = await audioEntryReader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        if (startEntry == null) {
            Log.LogWarning("Couldn't find start entry");
            return;
        }

        var playbackBlockEnd = cpuClock.Now - TimeSpan.FromDays(1); // Any time in past
        var playbackOffset = playbackBlockEnd - Moment.EpochStart; // now - playTime

        idRange = (startEntry.Id, idRange.End);
        var entries = audioEntryReader.Read(idRange, cancellationToken);
        var playProcesses = new ConcurrentDictionary<IMessageProcess<PlayTrackCommand>, Unit>();
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (!entry.StreamId.IsEmpty) // Streaming entry
                continue;
            if (entry.EndsAt < startAt)
                // We're normally starting @ (startAt - ChatConstants.MaxEntryDuration),
                // so we need to skip a few entries.
                continue;

            var now = cpuClock.Now;
            var entryBeginsAt = Moment.Max(entry.BeginsAt, startAt);
            var entryEndsAt = entry.EndsAt ?? entry.BeginsAt + InfDuration;
            entryEndsAt = Moment.Min(entryEndsAt, entry.ContentEndsAt ?? entryEndsAt);
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

            var playProcess = await EnqueueEntry(playAt, entry, skipTo, cancellationToken).ConfigureAwait(false);
            if (playProcess.WhenCompleted.IsCompleted)
                continue;

            playProcesses.TryAdd(playProcess, default);
            _ = playProcess.WhenCompleted.ContinueWith(
                t => playProcesses.TryRemove(playProcess, out _),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        await Task.WhenAll(playProcesses.Keys.Select(s => s.WhenCompleted)).ConfigureAwait(false);
    }
}
