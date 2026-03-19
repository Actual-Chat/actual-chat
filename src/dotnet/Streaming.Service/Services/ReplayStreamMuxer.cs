using ActualChat.Audio;
using ActualChat.Blobs;
using ActualChat.Live;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Reads historical chat entries, downloads their audio from blob storage,
/// and multiplexes them into a single <see cref="LiveStreamItem"/> output channel.
/// </summary>
public sealed class ReplayStreamMuxer : WorkerBase
{
    private readonly Channel<LiveStreamItem> _output;
    private int _nextStreamIndex;

    private IServiceProvider Services { get; }
    private Session Session { get; }
    private ChatId ChatId { get; }
    private Moment StartAt { get; }
    private TimeSpan Offset { get; }
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private AudioSourceDownloader AudioDownloader => field ??= Services.GetRequiredService<AudioSourceDownloader>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILogger Log => field ??= Services.LogFor<ReplayStreamMuxer>();

    public ChannelReader<LiveStreamItem> Output => _output.Reader;

    public ReplayStreamMuxer(
        IServiceProvider services,
        Session session,
        ChatId chatId,
        Moment startAt,
        TimeSpan offset)
    {
        Services = services;
        Session = session;
        ChatId = chatId;
        StartAt = startAt;
        Offset = offset;
        _output = ChannelExt.Create<LiveStreamItem>(ChannelExt.UnboundedFanInOptions);
        _ = Run(); // Start immediately
    }

    protected override Task OnStop()
    {
        _output.Writer.TryComplete();
        return Task.CompletedTask;
    }

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        try {
            Log.LogInformation("OnRun: Starting for chat {ChatId}, startAt={StartAt}, offset={Offset}",
                ChatId, StartAt, Offset);

            var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
            if (chat?.Rules.CanRead() != true) {
                Log.LogWarning("OnRun: Cannot read chat {ChatId}", ChatId);
                return;
            }

            var serverClock = Clocks.ServerClock;
            await serverClock.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);

            // Resolve actual start position
            var resolvedStartAt = await ResolveStartPosition(cancellationToken).ConfigureAwait(false);
            if (resolvedStartAt is null) {
                Log.LogInformation("OnRun: No audio entries found for chat {ChatId}", ChatId);
                return;
            }

            Log.LogInformation("OnRun: Resolved start position to {ResolvedStartAt}", resolvedStartAt.Value);

            // Stream entries from resolved position
            var streamStartedAt = serverClock.Now;
            var gapAdjustment = TimeSpan.Zero;
            var lastEntryEnd = resolvedStartAt.Value;
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
                .Where(x => x.HasAudio && !x.IsContentStreaming);

            await foreach (var entry in entries.ConfigureAwait(false)) {
                var entryEndsAt = entry.GetEndsAt();
                if (entryEndsAt < resolvedStartAt.Value)
                    continue;

                // Detect and skip gaps
                if (entry.BeginsAt > lastEntryEnd)
                    gapAdjustment += entry.BeginsAt - lastEntryEnd;

                // Pacing: check how far ahead we are of expected client playback
                var expectedPosition = resolvedStartAt.Value + gapAdjustment + (serverClock.Now - streamStartedAt);
                var aheadBy = (entry.BeginsAt - expectedPosition) / 2; // /2 for potential 2x playback

                if (aheadBy > TimeSpan.FromMinutes(1)) {
                    var waitFor = aheadBy - TimeSpan.FromSeconds(10);
                    Log.LogDebug("Pacing: waiting {WaitFor} (ahead by {AheadBy})", waitFor, aheadBy);
                    await Task.Delay(waitFor, cancellationToken).ConfigureAwait(false);
                }

                // Calculate skip for first entry
                var skipTo = (resolvedStartAt.Value - entry.BeginsAt).Positive();

                // PlaysAt = when this stream should start playing relative to the first stream
                var playsAt = (entry.BeginsAt - resolvedStartAt.Value - gapAdjustment).Positive();

                // Start streaming this entry (allows concurrent speakers)
                var streamIndex = Interlocked.Increment(ref _nextStreamIndex);
                var streamTask = ProcessEntry(entry, streamIndex, skipTo, playsAt, cancellationToken);
                streamTasks.Add(streamTask);

                // Clean up completed tasks
                streamTasks.RemoveAll(t => t.IsCompleted);

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
    }

    private async Task ProcessEntry(
        ChatEntry entry,
        int streamIndex,
        TimeSpan skipTo,
        TimeSpan playsAt,
        CancellationToken cancellationToken)
    {
        var frameCount = 0;
        try {
            if (entry.Audio is not { } audio) {
                Log.LogWarning("ProcessEntry: Entry {EntryId} has no audio metadata", entry.Id);
                return;
            }

            var blobId = audio.BlobId;
            if (blobId.IsNullOrEmpty()) {
                Log.LogWarning("ProcessEntry: Entry {EntryId} has no BlobId", entry.Id);
                return;
            }

            var audioSource = await AudioDownloader.Download(blobId, skipTo, cancellationToken)
                .ConfigureAwait(false);

            // Emit stream start
            var streamInfo = new LiveStreamInfo {
                ChatId = ChatId,
                AuthorId = entry.AuthorId,
                StreamId = audio.StreamId.NullIfEmpty() ?? blobId,
                BeginsAt = entry.BeginsAt + skipTo,
                Format = audioSource.Format,
                EntryId = entry.Id,
            };
            var startItem = new LiveStreamStart {
                StreamIndex = streamIndex,
                StreamInfo = streamInfo,
                PlaysAt = playsAt,
            };
            await _output.Writer.WriteAsync(startItem, cancellationToken).ConfigureAwait(false);

            // Emit audio frames
            await foreach (var frame in audioSource.GetFrames(cancellationToken).ConfigureAwait(false)) {
                var audioFrame = new LiveAudioFrame {
                    StreamIndex = streamIndex,
                    Data = frame.Data,
                };
                await _output.Writer.WriteAsync(audioFrame, cancellationToken).ConfigureAwait(false);
                frameCount++;
            }

            // Emit stream end
            var endItem = new LiveStreamEnd { StreamIndex = streamIndex };
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
                var endItem = new LiveStreamEnd { StreamIndex = streamIndex };
                await _output.Writer.WriteAsync(endItem, cancellationToken).ConfigureAwait(false);
            }
            catch {
                // Ignore
            }
        }
    }

    // Rewind/position resolution (moved from client-side ChatReplayer)

    private async Task<Moment?> ResolveStartPosition(CancellationToken cancellationToken)
    {
        if (Offset == TimeSpan.Zero)
            return await FindNearestAudioPosition(StartAt, cancellationToken).ConfigureAwait(false);

        return Offset < TimeSpan.Zero
            ? await ResolvePositionInPast(StartAt, Offset.Negate(), cancellationToken).ConfigureAwait(false)
            : await ResolvePositionInFuture(StartAt, Offset, cancellationToken).ConfigureAwait(false);
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

    private async Task<Moment?> ResolvePositionInFuture(Moment playingAt, TimeSpan offset, CancellationToken cancellationToken)
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
            if (entry.EndsAt < playingAt)
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

    private async Task<Moment?> ResolvePositionInPast(Moment playingAt, TimeSpan offset, CancellationToken cancellationToken)
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

        Range<long> idRange = (startEntry.LocalId, fullIdRange.End);
        var entries = entryReader.Read(idRange, cancellationToken);
        ChatEntry? lastEntry = null;
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (!entry.HasAudio || entry.IsContentStreaming)
                continue;
            if (entry.EndsAt >= playingAt) {
                lastEntry = entry;
                break;
            }
        }
        if (lastEntry == null)
            return null;

        idRange = ((Range<long>)(fullIdRange.Start, lastEntry.LocalId)).MoveEnd(1);
        var reverseEntries = entryReader.ReadReverse(idRange, cancellationToken);
        var remainingOffset = offset;
        var lastPlayingAt = playingAt;
        await foreach (var entry in reverseEntries.ConfigureAwait(false)) {
            if (!entry.HasAudio || entry.IsContentStreaming)
                continue;
            if (entry.BeginsAt >= playingAt)
                continue;

            var entryBeginsAt = entry.BeginsAt;
            var entryEndsAt = entry.EndsAt is { } endsAt
                ? Moment.Min(endsAt, lastPlayingAt)
                : lastPlayingAt;

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
