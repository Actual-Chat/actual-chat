using ActualChat.Audio;
using ActualChat.Live;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Reads historical chat entries, downloads their audio from blob storage,
/// and multiplexes them into a single <see cref="MuxedAudioStreamItem"/> output channel.
/// </summary>
public sealed class ReplayStreamMuxer : WorkerBase
{
    private readonly Channel<MuxedAudioStreamItem> _output;
    private int _nextStreamIndex;

    private IServiceProvider Services { get; }
    private Session Session { get; }
    private ChatId ChatId { get; }
    private Moment StartAt { get; }
    private TimeSpan RewindOffset { get; }
    private double Speed { get; }
    private Language? DubLanguage { get; }
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private AudioSourceDownloader AudioDownloader => field ??= Services.GetRequiredService<AudioSourceDownloader>();
    private ReplayDubs Dubs => field ??= Services.GetRequiredService<ReplayDubs>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private MomentClock SystemClock => Clocks.SystemClock;
    private ILogger Log => field ??= Services.LogFor<ReplayStreamMuxer>();

    public ChannelReader<MuxedAudioStreamItem> Output => _output.Reader;

    public ReplayStreamMuxer(
        IServiceProvider services,
        Session session,
        ChatId chatId,
        Moment startAt,
        TimeSpan rewindOffset,
        double speed = 1.0,
        Language? dubLanguage = null)
    {
        Services = services;
        Session = session;
        ChatId = chatId;
        StartAt = startAt;
        RewindOffset = rewindOffset;
        Speed = Math.Clamp(speed, 1.0, 2.0);
        DubLanguage = dubLanguage;
        _output = ChannelExt.Create<MuxedAudioStreamItem>(ChannelExt.UnboundedFanInOptions);
        _ = Run(); // Start immediately
    }

    protected override Task OnStop()
    {
        _output.Writer.TryComplete();
        return Task.CompletedTask;
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        var dubTasks = new Dictionary<ChatEntryId, Task<ReplayDub?>>();
        try {
            Log.LogInformation("OnRun: Starting for chat {ChatId}, startAt={StartAt}, rewindOffset={RewindOffset}",
                ChatId, StartAt, RewindOffset);

            var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
            if (chat?.Rules.CanRead() != true) {
                Log.LogWarning("OnRun: Cannot read chat {ChatId}", ChatId);
                return;
            }

            // Resolve actual start position
            var resolvedStartAt = await ResolveStartPosition(cancellationToken).ConfigureAwait(false);
            if (resolvedStartAt is null) {
                Log.LogInformation("OnRun: No audio entries found for chat {ChatId}", ChatId);
                return;
            }

            Log.LogInformation("OnRun: Resolved start position to {ResolvedStartAt}", resolvedStartAt.Value);

            // Stream entries from resolved position
            var streamStartedAt = SystemClock.Now;
            var gapAdjustment = TimeSpan.Zero;
            var lastEntryEnd = resolvedStartAt.Value;
            var notBefore = TimeSpan.Zero;
            var streamTasks = new List<Task>();

            var entryReader = new ChatEntryReader(Chats, Session, ChatId);
            var idRange = await Chats.GetIdRange(Session, ChatId, cancellationToken).ConfigureAwait(false);
            var startEntry = await entryReader
                .FindByMinBeginsAt(resolvedStartAt.Value - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
                .ConfigureAwait(false);
            if (startEntry == null) {
                Log.LogWarning("OnRun: Couldn't find start entry");
                return;
            }

            idRange = (startEntry.LocalId, idRange.End);
            var entries = entryReader.Read(idRange, cancellationToken)
                .Where(x => x.HasAudio && !x.IsContentStreaming && x.GetEndsAt() >= resolvedStartAt.Value);

            await foreach (var entry in WithDubLookahead(entries, dubTasks, cancellationToken).ConfigureAwait(false)) {
                var entryEndsAt = entry.GetEndsAt();

                // Detect and skip gaps: only count time after lastEntryEnd
                // that isn't covered by this entry's audio range
                var gapStart = Moment.Max(lastEntryEnd, resolvedStartAt.Value);
                if (entry.BeginsAt > gapStart)
                    gapAdjustment += entry.BeginsAt - gapStart;

                // Pacing: check how far ahead we are of expected client playback
                var expectedPosition = resolvedStartAt.Value + gapAdjustment
                    + (SystemClock.Now - streamStartedAt) * Speed;
                var aheadBy = (entry.BeginsAt - expectedPosition) / 2; // /2 for safety margin

                if (aheadBy > TimeSpan.FromMinutes(1)) {
                    var waitFor = aheadBy - TimeSpan.FromSeconds(10);
                    Log.LogDebug("Pacing: waiting {WaitFor} (ahead by {AheadBy})", waitFor, aheadBy);
                    await Task.Delay(waitFor, cancellationToken).ConfigureAwait(false);
                }

                // Calculate skip for first entry
                var skipTo = (resolvedStartAt.Value - entry.BeginsAt).Positive();

                // PlaysAt = when this stream should start playing relative to the first stream,
                // divided by speed so entries start proportionally sooner at higher speeds
                var timelinePlaysAt = (entry.BeginsAt - resolvedStartAt.Value - gapAdjustment).Positive() / Speed;
                var dub = DubLanguage == null
                    ? null
                    : await GetDub(entry, dubTasks, cancellationToken).ConfigureAwait(false);
                // A dub's spoken length rarely matches the source's, so later entries in a dubbed
                // replay are pushed out to never start before the previous one finished playing;
                // an undubbed replay (no DubLanguage) keeps concurrent speakers concurrent, as before
                var playsAt = ReplayTimeline.PlaysAt(timelinePlaysAt, notBefore, DubLanguage != null);
                var streamIndex = Interlocked.Increment(ref _nextStreamIndex);
                if (dub?.Live != null) {
                    // How long a dub still being spoken lasts is known only once it's over, so the
                    // entry is streamed to its end before the next one is placed on the timeline;
                    // the dub's skip is the source's as-is, assuming the two run at the same pace
                    var playedDuration = await ProcessEntry(
                        entry, dub, skipTo, streamIndex, skipTo, playsAt, cancellationToken).ConfigureAwait(false);
                    notBefore = playsAt + playedDuration / Speed;
                }
                else {
                    var stored = dub?.Stored;
                    var dubSkipTo = stored != null
                        ? ReplayTimeline.ScaleSkip(
                            skipTo, entryEndsAt - entry.BeginsAt, stored.EndsAt - stored.BeginsAt)
                        : TimeSpan.Zero;
                    if (DubLanguage != null) {
                        var playedDuration = stored != null
                            ? (stored.EndsAt - stored.BeginsAt) - dubSkipTo
                            : entryEndsAt - entry.BeginsAt - skipTo;
                        notBefore = playsAt + playedDuration.Positive() / Speed;
                    }

                    // Start streaming this entry (allows concurrent speakers)
                    var streamTask = ProcessEntry(
                        entry, dub, dubSkipTo, streamIndex, skipTo, playsAt, cancellationToken);
                    streamTasks.Add(streamTask);

                    // Clean up completed tasks
                    streamTasks.RemoveAll(t => t.IsCompleted);
                }

                lastEntryEnd = Moment.Max(lastEntryEnd, entryEndsAt);
            }

            // Wait for all remaining stream tasks
            if (streamTasks.Count > 0)
                await Task.WhenAll(streamTasks).ConfigureAwait(false);

            Log.LogInformation("OnRun: Replay completed for chat {ChatId}", ChatId);
        }
        catch (Exception e) when (!e.IsCancellationOf(StopToken)) {
            Log.LogError(e, "OnRun: Failed for chat {ChatId}", ChatId);
        }
        finally {
            // Lookahead dubs still in flight when the replay stops early would otherwise fault
            // unobserved - their token is this method's own, so they're already unwinding
            foreach (var dubTask in dubTasks.Values)
                await dubTask.SilentAwait(false);
        }
    }

    // Returns the raw duration streamed - the last frame's end offset, before any speed-up drops
    private async Task<TimeSpan> ProcessEntry(
        ChatEntry entry,
        ReplayDub? dub,
        TimeSpan dubSkipTo,
        int streamIndex,
        TimeSpan skipTo,
        TimeSpan playsAt,
        CancellationToken cancellationToken)
    {
        var frameCount = 0;
        var streamedDuration = TimeSpan.Zero;
        try {
            if (entry.Audio is not { } audio) {
                Log.LogWarning("ProcessEntry: Entry {EntryId} has no audio metadata", entry.Id);
                return streamedDuration;
            }

            var blobId = audio.BlobId;
            if (blobId.IsNullOrEmpty()) {
                Log.LogWarning("ProcessEntry: Entry {EntryId} has no BlobId", entry.Id);
                return streamedDuration;
            }

            var audioSource = dub != null
                ? await TryOpenDub(dub, dubSkipTo, entry, cancellationToken).ConfigureAwait(false)
                : null;
            if (audioSource == null) {
                dub = null; // No dub audio (or an error opening it) - fall back to the original
                audioSource = await AudioDownloader.Download(blobId, skipTo, cancellationToken).ConfigureAwait(false);
            }

            // Emit stream start
            var streamInfo = new LiveAudioStreamInfo {
                ChatId = ChatId,
                AuthorId = entry.AuthorId,
                StreamId = audio.StreamId.NullIfEmpty() ?? blobId,
                BeginsAt = entry.BeginsAt + skipTo,
                SourceBeginsAt = entry.BeginsAt + skipTo,
                Format = audioSource.Format,
                EntryId = entry.Id,
            };
            if (dub != null)
                streamInfo = streamInfo with { DubLanguage = DubLanguage };
            var startItem = new MuxedAudioStreamStart {
                StreamIndex = streamIndex,
                StreamInfo = streamInfo,
                PlaysAt = playsAt,
            };
            await _output.Writer.WriteAsync(startItem, cancellationToken).ConfigureAwait(false);

            // Emit audio frames, skipping some for speedup
            // For speed S, we keep 1/S fraction of frames.
            // E.g. 1.5x → skip every 3rd frame (keep 2 of 3),
            //      2.0x → skip every 2nd frame (keep 1 of 2).
            var skipInterval = Speed > 1.0 ? (int)Math.Round(Speed / (Speed - 1.0)) : 0;
            var rawFrameIndex = 0;
            await foreach (var frame in audioSource.GetFrames(cancellationToken).ConfigureAwait(false)) {
                rawFrameIndex++;
                streamedDuration = frame.Offset + frame.Duration;
                if (skipInterval > 0 && rawFrameIndex % skipInterval == 0)
                    continue; // Skip this frame for speedup

                var audioFrame = new MuxedAudioFrame {
                    StreamIndex = streamIndex,
                    Data = frame.Data,
                    Offset = frame.Offset,
                };
                await _output.Writer.WriteAsync(audioFrame, cancellationToken).ConfigureAwait(false);
                frameCount++;
            }

            // Emit stream end
            var endItem = new MuxedAudioStreamEnd { StreamIndex = streamIndex };
            await _output.Writer.WriteAsync(endItem, cancellationToken).ConfigureAwait(false);
            Log.LogDebug("Entry {EntryId}: completed, {FrameCount} frames", entry.Id, frameCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            Log.LogDebug("Entry stream #{StreamIndex} cancelled after {FrameCount} frames", streamIndex, frameCount);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Error processing entry {EntryId}, {FrameCount} frames emitted",
                entry.Id, frameCount);

            // Still emit end marker on error
            try {
                var endItem = new MuxedAudioStreamEnd { StreamIndex = streamIndex };
                await _output.Writer.WriteAsync(endItem, cancellationToken).ConfigureAwait(false);
            }
            catch {
                // Ignore
            }
        }
        return streamedDuration;
    }

    private async Task<AudioSource?> TryOpenDub(
        ReplayDub dub, TimeSpan dubSkipTo, ChatEntry entry, CancellationToken cancellationToken)
    {
        try {
            if (dub.Live is not { } live)
                return await AudioDownloader.TryDownload(dub.Stored!.BlobId, dubSkipTo, cancellationToken)
                    .ConfigureAwait(false);

            // A synthesis that fails or speaks nothing shows up on its first frame; peeking is
            // free, as the source memoizes its frames and GetFrames replays them from the start
            var source = live.SkipTo(dubSkipTo, cancellationToken);
            if (await source.GetFrames(cancellationToken).AnyAsync(cancellationToken).ConfigureAwait(false))
                return source;

            Log.LogInformation(
                "ProcessEntry: {Language} dub for entry {EntryId} is being made but has no audio, serving the original",
                DubLanguage, entry.Id);
            return null;
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogInformation(e,
                "ProcessEntry: {Language} dub for entry {EntryId} failed to open, serving the original",
                DubLanguage, entry.Id);
            return null;
        }
    }

    // Dubbing lookahead

    // Yields entries in order while keeping dub synthesis running ReplayDubLookahead entries ahead,
    // so a dub is normally ready by the time its entry's turn to stream comes up
    private async IAsyncEnumerable<ChatEntry> WithDubLookahead(
        IAsyncEnumerable<ChatEntry> entries,
        Dictionary<ChatEntryId, Task<ReplayDub?>> dubTasks,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var queue = new Queue<ChatEntry>();
        await using var enumerator = entries.GetAsyncEnumerator(cancellationToken);

        while (queue.Count < Constants.Audio.ReplayDubLookahead
            && await TryEnqueueNext(enumerator, queue, dubTasks, cancellationToken).ConfigureAwait(false)) { }

        while (queue.Count > 0) {
            var entry = queue.Dequeue();
            await TryEnqueueNext(enumerator, queue, dubTasks, cancellationToken).ConfigureAwait(false);
            yield return entry;
        }
    }

    private async Task<bool> TryEnqueueNext(
        IAsyncEnumerator<ChatEntry> enumerator,
        Queue<ChatEntry> queue,
        Dictionary<ChatEntryId, Task<ReplayDub?>> dubTasks,
        CancellationToken cancellationToken)
    {
        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
            return false;

        var next = enumerator.Current;
        queue.Enqueue(next);
        if (DubLanguage != null)
            dubTasks[next.Id] = Dubs.GetOrCreate(next, DubLanguage, cancellationToken);
        return true;
    }

    private Task<ReplayDub?> GetDub(
        ChatEntry entry,
        Dictionary<ChatEntryId, Task<ReplayDub?>> dubTasks,
        CancellationToken cancellationToken)
        => dubTasks.Remove(entry.Id, out var task)
            ? task
            : Dubs.GetOrCreate(entry, DubLanguage!, cancellationToken);

    // Rewind/position resolution (moved from client-side ChatReplayer)

    private async Task<Moment?> ResolveStartPosition(CancellationToken cancellationToken)
    {
        if (RewindOffset == TimeSpan.Zero)
            return await FindNearestAudioPosition(StartAt, cancellationToken).ConfigureAwait(false);

        return RewindOffset < TimeSpan.Zero
            ? await ResolvePositionInPast(StartAt, RewindOffset.Negate(), cancellationToken).ConfigureAwait(false)
            : await ResolvePositionInFuture(StartAt, RewindOffset, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Moment?> FindNearestAudioPosition(Moment startAt, CancellationToken cancellationToken)
    {
        var entryReader = new ChatEntryReader(Chats, Session, ChatId);
        var idRange = await Chats.GetIdRange(Session, ChatId, cancellationToken).ConfigureAwait(false);
        var startEntry = await entryReader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);

        if (startEntry == null)
            return null;

        // Check if startAt falls within or after an audio entry
        idRange = (startEntry.LocalId, idRange.End);
        var entries = entryReader.Read(idRange, cancellationToken)
            .Where(x => x.HasAudio && !x.IsContentStreaming);

        await foreach (var entry in entries.ConfigureAwait(false)) {
            var entryEndsAt = entry.GetEndsAt();
            if (entryEndsAt >= startAt)
                return startAt; // startAt is within or at an audio entry range
        }

        return null; // No audio entries at or after startAt
    }

    private async Task<Moment?> ResolvePositionInFuture(
        Moment playingAt, TimeSpan offset, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(offset, TimeSpan.Zero);
        if (offset == TimeSpan.Zero)
            return playingAt;

        var entryReader = new ChatEntryReader(Chats, Session, ChatId);
        var idRange = await Chats.GetIdRange(Session, ChatId, cancellationToken).ConfigureAwait(false);
        var startEntry = await entryReader
            .FindByMinBeginsAt(playingAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        if (startEntry == null)
            return null;

        idRange = (startEntry.LocalId, idRange.End);
        var entries = entryReader.Read(idRange, cancellationToken);
        var remainingOffset = offset;
        var lastPlayingAt = playingAt;
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (!entry.HasAudio || entry.IsContentStreaming)
                continue;
            if (entry.GetEndsAt() < playingAt)
                continue;

            var entryBeginsAt = Moment.Max(entry.BeginsAt, lastPlayingAt);
            var entryEndsAt = entry.GetEndsAt();

            var expectedRewindPosition = entryBeginsAt + remainingOffset;
            if (expectedRewindPosition <= entryEndsAt)
                return expectedRewindPosition;
            var shiftDuration = entryEndsAt - entryBeginsAt;
            remainingOffset -= shiftDuration;
            lastPlayingAt = entryEndsAt;
        }
        return lastPlayingAt;
    }

    private async Task<Moment?> ResolvePositionInPast(
        Moment playingAt, TimeSpan offset, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(offset, TimeSpan.Zero);
        if (offset == TimeSpan.Zero)
            return playingAt;

        var entryReader = new ChatEntryReader(Chats, Session, ChatId);
        var fullIdRange = await Chats.GetIdRange(Session, ChatId, cancellationToken).ConfigureAwait(false);
        var startEntry = await entryReader
            .FindByMinBeginsAt(playingAt - Constants.Chat.MaxEntryDuration, fullIdRange, cancellationToken)
            .ConfigureAwait(false);
        if (startEntry == null)
            return null;

        Range<long> lidRange = (startEntry.LocalId, fullIdRange.End);
        var entries = entryReader.Read(lidRange, cancellationToken);
        ChatEntry? lastEntry = null;
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (!entry.HasAudio || entry.IsContentStreaming)
                continue;
            if (entry.GetEndsAt() >= playingAt) {
                lastEntry = entry;
                break;
            }
        }
        if (lastEntry == null)
            return null;

        lidRange = ((Range<long>)(fullIdRange.Start, lastEntry.LocalId)).MoveEnd(1);
        var reverseEntries = entryReader.ReadReverse(lidRange, cancellationToken);
        var remainingOffset = offset;
        var lastPlayingAt = playingAt;
        await foreach (var entry in reverseEntries.ConfigureAwait(false)) {
            if (!entry.HasAudio || entry.IsContentStreaming)
                continue;
            if (entry.BeginsAt >= playingAt)
                continue;

            var entryBeginsAt = entry.BeginsAt;
            var entryEndsAt = Moment.Min(entry.GetEndsAt(), lastPlayingAt);

            var expectedRewindPosition = entryEndsAt - remainingOffset;
            if (expectedRewindPosition >= entryBeginsAt)
                return expectedRewindPosition;
            var shiftDuration = entryEndsAt - entryBeginsAt;
            remainingOffset -= shiftDuration;
            lastPlayingAt = entryBeginsAt;
        }
        return lastPlayingAt;
    }
}
