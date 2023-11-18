namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class HistoricalChatPlayer : ChatPlayer
{
    public HistoricalChatPlayer(ChatHub chatHub, ChatId chatId)
        : base(chatHub, chatId)
        => PlayerKind = ChatPlayerKind.Historical;

    protected override async Task Play(
        ChatEntryPlayer entryPlayer, Moment minPlayAt, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        if (chat == null || !chat.Rules.CanRead())
            return;

        Operation = $"historical playback in \"{chat.Title}\"";
        var audioEntryReader = ChatHub.NewEntryReader(ChatId, ChatEntryKind.Audio);
        var idRange = await Chats.GetIdRange(Session, ChatId, ChatEntryKind.Audio, cancellationToken)
            .ConfigureAwait(false);
        var startEntry = await audioEntryReader
            .FindByMinBeginsAt(minPlayAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        if (startEntry == null) {
            Log.LogWarning("Couldn't find start entry");
            return;
        }

        var clock = Clocks.CpuClock;
        var initialSleepAndPauseDuration = SleepAndPauseDuration;
        var realStartAt = RealNow();
        var lastPlaybackBlockEnd = PlaybackNow(); // Any time in past, actually

        idRange = (startEntry.LocalId, idRange.End);
        var entries = audioEntryReader.Read(idRange, cancellationToken);
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (entry.IsStreaming)
                continue;

            var playbackNow = PlaybackNow();
            var entryEndsAt = entry.EndsAt ?? entry.BeginsAt + MaxEntryDuration;
            if (entryEndsAt < playbackNow)
                continue;

            if (lastPlaybackBlockEnd < entry.BeginsAt) {
                // There is a gap between the last playing "block" and the entry,
                // so we should move forward minPlayAt to skip it.
                minPlayAt += entry.BeginsAt - lastPlaybackBlockEnd; // We must re-sync playbackNow after this!
            }
            lastPlaybackBlockEnd = Moment.Max(entryEndsAt, lastPlaybackBlockEnd);

            if (!await CanContinuePlayback(cancellationToken).ConfigureAwait(false))
                return;

            playbackNow = PlaybackNow(); // Re-sync to account for sleep during CanContinuePlayback & possible minPlayAt update above
            var enqueueDelay = (entry.BeginsAt - playbackNow - EnqueueAheadDuration).Positive();
            if (enqueueDelay > TimeSpan.Zero) {
                Log.LogInformation("Play: delaying #{EntryId} for {EnqueueDelay}", entry.Id.Value, enqueueDelay);
                await EnqueueDelay(enqueueDelay, cancellationToken).ConfigureAwait(false);
                playbackNow = PlaybackNow(); // Re-sync to account for sleep during EnqueueDelay
            }

            if (entryEndsAt < playbackNow)
                continue;

            var skipOffset = playbackNow - entry.BeginsAt;
            var skipTo = skipOffset.Positive();
            var playAt = clock.Now + (-skipOffset).Positive();
            DebugLog?.LogDebug("Play: enqueuing #{EntryId} @ {SkipTo}", entry.Id, skipTo.ToShortString());
            entryPlayer.EnqueueEntry(entry, skipTo, playAt);
        }

        Moment RealNow() => Clocks.CpuClock.Now + initialSleepAndPauseDuration - SleepAndPauseDuration;
        TimeSpan PlaybackDuration() => RealNow() - realStartAt;
        Moment PlaybackNow() => minPlayAt + PlaybackDuration();
    }

    public Task<Moment?> GetRewindMoment(Moment playingAt, TimeSpan shift, CancellationToken cancellationToken)
    {
        if (shift == TimeSpan.Zero)
            return Task.FromResult<Moment?>(playingAt);
        if (shift.Ticks < 0)
            return GetRewindMomentInPast(playingAt, shift.Negate(), cancellationToken);

        return GetRewindMomentInFuture(playingAt, shift, cancellationToken);
    }

    private async Task<Moment?> GetRewindMomentInFuture(Moment playingAt, TimeSpan shift, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(shift, TimeSpan.Zero, nameof(shift));

        var audioEntryReader = ChatHub.NewEntryReader(ChatId, ChatEntryKind.Audio);
        var idRange = await Chats.GetIdRange(Session, ChatId, ChatEntryKind.Audio, cancellationToken)
            .ConfigureAwait(false);
        var startEntry = await audioEntryReader
            .FindByMinBeginsAt(playingAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        if (startEntry == null) {
            Log.LogWarning("Couldn't find start entry");
            return null;
        }

        idRange = (startEntry.LocalId, idRange.End);
        var entries = audioEntryReader.Read(idRange, cancellationToken);
        var remainedShift = shift;
        var lastShiftPosition = playingAt;
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (entry.IsStreaming) // Streaming entry
                continue;
            if (entry.EndsAt < playingAt)
                // We're normally starting @ (playingAt - ChatConstants.MaxEntryDuration),
                // so we need to skip a few entries.
                continue;

            var entryBeginsAt = Moment.Max(entry.BeginsAt, lastShiftPosition);
            var entryEndsAt = entry.EndsAt ?? entry.BeginsAt + MaxEntryDuration;

            var expectedRewindPosition = entryBeginsAt + remainedShift;
            if (expectedRewindPosition <= entryEndsAt)
                return expectedRewindPosition;
            var shiftDuration = entryEndsAt - entryBeginsAt;
            remainedShift -= shiftDuration;
            lastShiftPosition = entryEndsAt;
        }
        return lastShiftPosition; // return max position that we reached
    }

    private async Task<Moment?> GetRewindMomentInPast(Moment playingAt, TimeSpan shift, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(shift, TimeSpan.Zero, nameof(shift));

        var audioEntryReader = ChatHub.NewEntryReader(ChatId, ChatEntryKind.Audio);
        var fullIdRange = await Chats.GetIdRange(Session, ChatId, ChatEntryKind.Audio, cancellationToken)
            .ConfigureAwait(false);
        var startEntry = await audioEntryReader
            .FindByMinBeginsAt(playingAt - Constants.Chat.MaxEntryDuration, fullIdRange, cancellationToken)
            .ConfigureAwait(false);
        if (startEntry == null) {
            Log.LogWarning("Couldn't find start entry");
            return null;
        }

        Range<long> idRange = (startEntry.LocalId, fullIdRange.End);
        var entries = audioEntryReader.Read(idRange, cancellationToken);
        ChatEntry? lastEntry = null;
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (!entry.StreamId.IsEmpty) // Streaming entry
                continue;
            if (entry.EndsAt >= playingAt) {
                // We're normally starting @ (playingAt - ChatConstants.MaxEntryDuration),
                // so we need to find an entry that completes after @ playingAt.
                lastEntry = entry;
                break;
            }
        }
        if (lastEntry == null) {
            Log.LogWarning("Couldn't find last entry");
            return null;
        }

        idRange = ((Range<long>)(fullIdRange.Start, lastEntry.LocalId)).MoveEnd(1);
        var reverseEntries = audioEntryReader.ReadReverse(idRange, cancellationToken);
        var remainedShift = shift;
        var lastShiftPosition = playingAt;
        await foreach (var entry in reverseEntries.ConfigureAwait(false)) {
            if (!entry.StreamId.IsEmpty) // Streaming entry
                continue;
            if (entry.BeginsAt >= playingAt)
                // We're normally should not enter here due to way how last entry is looked up.
                continue;

            var entryBeginsAt = entry.BeginsAt;
            var entryEndsAt = entry.EndsAt.HasValue
                ? Moment.Min(entry.EndsAt.Value, lastShiftPosition)
                : lastShiftPosition;

            var expectedRewindPosition = entryEndsAt - remainedShift;
            if (expectedRewindPosition >= entryBeginsAt)
                return expectedRewindPosition;
            var shiftDuration = entryEndsAt - entryBeginsAt;
            remainedShift -= shiftDuration;
            lastShiftPosition = entryBeginsAt;
        }
        return lastShiftPosition; // return min position that we reached
    }

    private async Task EnqueueDelay(TimeSpan delay, CancellationToken cancellationToken)
    {
        // Waits for enqueue delay.
        // If pause or sleep is activated during enqueue delay,
        // enqueue delay is extended by the duration of the pause.
        while (true) {
            delay = delay.Positive();
            if (delay <= TimeSpan.FromMilliseconds(50)) {
                // Extremely short delays increase the CPU load,
                // but don't add much of extra value here, coz all we want
                // is to avoid scheduling of too many playbacks in advance.
                return;
            }

            var initialSleepAndPauseDuration = SleepAndPauseDuration;
            var startedAt = CpuTimestamp.Now;
            await Clocks.CpuClock.Delay(delay, cancellationToken).ConfigureAwait(false);
            await Playback.IsPaused.When(x => !x, cancellationToken).ConfigureAwait(false);
            var actualDelay = startedAt.Elapsed - SleepAndPauseDuration + initialSleepAndPauseDuration;
            delay -= actualDelay;
        }
    }
}
