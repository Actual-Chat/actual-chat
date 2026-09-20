using ActualChat.Audio;
using ActualChat.Live;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Reads historical chat entries, downloads their audio from blob storage,
/// and multiplexes them into a single <see cref="MuxedAudioStreamItem"/> output channel.
/// Frames go out in the order they play, at most <see cref="Constants.Audio.ReplayMaxLead"/> early.
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
        var streams = new List<EntryStream>();
        IAsyncEnumerator<ChatEntry>? upcoming = null;
        Task<(bool HasEntry, EntryStream? Stream)>? nextTask = null;
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
            upcoming = WithDubLookahead(entries, dubTasks, cancellationToken).GetAsyncEnumerator(cancellationToken);
            var timeline = new TimelineState(resolvedStartAt.Value);
            var streamStartedAt = SystemClock.Now;
            var hasMoreEntries = true;
            while (true) {
                // Where the entry after a dub still being spoken goes is known only once that dub ends
                if (nextTask == null && hasMoreEntries && !streams.Any(x => x.IsLiveDub))
                    nextTask = PrepareNext(upcoming, dubTasks, timeline, cancellationToken);

                var current = streams.MinBy(x => x.Deadline);
                // The next entry is waited for only when nothing else can be sent meanwhile
                if (nextTask != null && (current == null || nextTask.IsCompleted)) {
                    var (hasEntry, next) = await nextTask.ConfigureAwait(false);
                    if (!hasEntry) {
                        hasMoreEntries = false;
                        nextTask = null;
                        continue;
                    }
                    if (next == null || current == null || next.PlaysAt <= current.Deadline) {
                        nextTask = null;
                        if (next != null && await Admit(next, cancellationToken).ConfigureAwait(false))
                            streams.Add(next);
                        continue;
                    }
                }
                if (current == null)
                    break;

                var lead = current.Deadline - (SystemClock.Now - streamStartedAt);
                if (lead > Constants.Audio.ReplayMaxLead)
                    await Task.Delay(lead - Constants.Audio.ReplayMaxLead / 2, cancellationToken).ConfigureAwait(false);
                if (await SendFrameAndAdvance(current, cancellationToken).ConfigureAwait(false))
                    continue;

                streams.Remove(current);
                await current.DisposeAsync().ConfigureAwait(false);
                if (current.IsLiveDub)
                    timeline.NotBefore = current.PlaysAt + current.StreamedDuration / Speed;
            }

            Log.LogInformation("OnRun: Replay completed for chat {ChatId}", ChatId);
        }
        catch (Exception e) when (!e.IsCancellationOf(StopToken)) {
            Log.LogError(e, "OnRun: Failed for chat {ChatId}", ChatId);
        }
        finally {
            // The pending entry uses the enumerator and the dub tasks, so it's awaited before either goes
            if (nextTask != null && (await nextTask.ResultAwait(false)).ValueOrDefault.Stream is { } pending)
                streams.Add(pending);

            foreach (var stream in streams)
                await stream.DisposeAsync().ConfigureAwait(false);
            if (upcoming != null)
                await upcoming.DisposeSilentlyAsync().ConfigureAwait(false);
            // Lookahead dubs still in flight when the replay stops early would otherwise fault
            // unobserved - their token is this method's own, so they're already unwinding
            foreach (var dubTask in dubTasks.Values)
                await dubTask.SilentAwait(false);
        }
    }

    // Private methods

    // HasEntry is false once the entries run out; an entry with nothing to play comes with a null Stream
    private async Task<(bool HasEntry, EntryStream? Stream)> PrepareNext(
        IAsyncEnumerator<ChatEntry> upcoming,
        Dictionary<ChatEntryId, Task<ReplayDub?>> dubTasks,
        TimelineState timeline,
        CancellationToken cancellationToken)
    {
        if (!await upcoming.MoveNextAsync().ConfigureAwait(false))
            return (false, null);

        var entry = upcoming.Current;
        var entryEndsAt = entry.GetEndsAt();
        // The pause before the first entry is skipped whole, later ones are cut down to ReplayMaxGap
        var gap = entry.BeginsAt - Moment.Max(timeline.LastEntryEnd ?? timeline.StartAt, timeline.StartAt);
        timeline.SkippedDuration += timeline.LastEntryEnd == null
            ? gap.Positive()
            : ReplayTimeline.SkippedGap(gap, Constants.Audio.ReplayMaxGap);
        timeline.LastEntryEnd = Moment.Max(timeline.LastEntryEnd ?? entryEndsAt, entryEndsAt);

        var skipTo = (timeline.StartAt - entry.BeginsAt).Positive();
        // PlaysAt = when this stream should start playing relative to the first stream,
        // divided by speed so entries start proportionally sooner at higher speeds
        var timelinePlaysAt = (entry.BeginsAt - timeline.StartAt - timeline.SkippedDuration).Positive() / Speed;
        var dub = DubLanguage == null
            ? null
            : await GetDub(entry, dubTasks, cancellationToken).ConfigureAwait(false);
        // A dub's spoken length rarely matches the source's, so later entries in a dubbed
        // replay are pushed out to never start before the previous one finished playing;
        // an undubbed replay (no DubLanguage) keeps concurrent speakers concurrent, as before
        var playsAt = ReplayTimeline.PlaysAt(timelinePlaysAt, timeline.NotBefore, DubLanguage != null);
        var streamIndex = Interlocked.Increment(ref _nextStreamIndex);
        if (dub?.Live != null) {
            // How long a dub still being spoken lasts is known only once it's over, so the entry
            // streams to its end before the next one is placed; the dub's skip is the source's as-is,
            // assuming the two run at the same pace
            var liveOpenTask = OpenAudio(entry, dub, skipTo, skipTo, cancellationToken);
            return (true, new EntryStream(entry, streamIndex, playsAt, skipTo, Speed, liveOpenTask) {
                IsLiveDub = true,
            });
        }

        var stored = dub?.Stored;
        var dubSkipTo = stored != null
            ? ReplayTimeline.ScaleSkip(skipTo, entryEndsAt - entry.BeginsAt, stored.EndsAt - stored.BeginsAt)
            : TimeSpan.Zero;
        if (DubLanguage != null) {
            var playedDuration = stored != null
                ? (stored.EndsAt - stored.BeginsAt) - dubSkipTo
                : entryEndsAt - entry.BeginsAt - skipTo;
            timeline.NotBefore = playsAt + playedDuration.Positive() / Speed;
        }
        var openTask = OpenAudio(entry, dub, dubSkipTo, skipTo, cancellationToken);
        return (true, new EntryStream(entry, streamIndex, playsAt, skipTo, Speed, openTask));
    }

    private async Task<OpenedAudio?> OpenAudio(
        ChatEntry entry,
        ReplayDub? dub,
        TimeSpan dubSkipTo,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        if (entry.Audio is not { } audio) {
            Log.LogWarning("OpenAudio: Entry {EntryId} has no audio metadata", entry.Id);
            return null;
        }

        var blobId = audio.BlobId;
        if (blobId.IsNullOrEmpty()) {
            Log.LogWarning("OpenAudio: Entry {EntryId} has no BlobId", entry.Id);
            return null;
        }

        var audioSource = dub != null
            ? await TryOpenDub(dub, dubSkipTo, entry, cancellationToken).ConfigureAwait(false)
            : null;
        if (audioSource != null)
            return new OpenedAudio(audioSource, dub);

        // No dub audio (or an error opening it) - fall back to the original
        audioSource = await AudioDownloader.Download(blobId, skipTo, cancellationToken).ConfigureAwait(false);
        return new OpenedAudio(audioSource, null);
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
                "OpenAudio: {Language} dub for entry {EntryId} is being made but has no audio, serving the original",
                DubLanguage, entry.Id);
            return null;
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogInformation(e,
                "OpenAudio: {Language} dub for entry {EntryId} failed to open, serving the original",
                DubLanguage, entry.Id);
            return null;
        }
    }

    // Returns false when the entry has nothing to stream
    private async Task<bool> Admit(EntryStream stream, CancellationToken cancellationToken)
    {
        var entry = stream.Entry;
        try {
            if (await stream.Open(cancellationToken).ConfigureAwait(false) is not { } opened) {
                await stream.DisposeAsync().ConfigureAwait(false);
                return false;
            }

            var audio = entry.Audio!;
            var streamInfo = new LiveAudioStreamInfo {
                ChatId = ChatId,
                AuthorId = entry.AuthorId,
                StreamId = audio.StreamId.NullIfEmpty() ?? audio.BlobId,
                BeginsAt = entry.BeginsAt + stream.SkipTo,
                SourceBeginsAt = entry.BeginsAt + stream.SkipTo,
                Format = opened.Source.Format,
                EntryId = entry.Id,
            };
            if (opened.Dub != null)
                streamInfo = streamInfo with { DubLanguage = DubLanguage };
            var startItem = new MuxedAudioStreamStart {
                StreamIndex = stream.StreamIndex,
                StreamInfo = streamInfo,
                PlaysAt = stream.PlaysAt,
            };
            await _output.Writer.WriteAsync(startItem, cancellationToken).ConfigureAwait(false);
            if (await stream.MoveNext().ConfigureAwait(false))
                return true;

            await WriteEnd(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Admit: Error opening entry {EntryId}", entry.Id);
            await WriteEnd(stream, cancellationToken).SilentAwait(false);
        }
        await stream.DisposeAsync().ConfigureAwait(false);
        return false;
    }

    // Returns false once the stream is over - its end marker is sent by then
    private async Task<bool> SendFrameAndAdvance(EntryStream stream, CancellationToken cancellationToken)
    {
        try {
            var frame = stream.Frame;
            var audioFrame = new MuxedAudioFrame {
                StreamIndex = stream.StreamIndex,
                Data = frame.Data,
                Offset = frame.Offset,
            };
            await _output.Writer.WriteAsync(audioFrame, cancellationToken).ConfigureAwait(false);
            if (await stream.MoveNext().ConfigureAwait(false))
                return true;

            Log.LogDebug("Entry {EntryId}: completed, {FrameCount} frames", stream.Entry.Id, stream.FrameCount);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Error processing entry {EntryId}, {FrameCount} frames emitted",
                stream.Entry.Id, stream.FrameCount);
        }
        await WriteEnd(stream, cancellationToken).SilentAwait(false);
        return false;
    }

    private ValueTask WriteEnd(EntryStream stream, CancellationToken cancellationToken)
        => _output.Writer.WriteAsync(new MuxedAudioStreamEnd { StreamIndex = stream.StreamIndex }, cancellationToken);

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

    private async Task<ReplayDub?> GetDub(
        ChatEntry entry,
        Dictionary<ChatEntryId, Task<ReplayDub?>> dubTasks,
        CancellationToken cancellationToken)
    {
        var dub = dubTasks.Remove(entry.Id, out var task)
            ? await task.ConfigureAwait(false)
            : ReplayDub.Pending;
        // A lookahead wait that ran out decided nothing: by now the dub may well be there
        if (dub is { IsPending: true })
            dub = await Dubs.GetOrCreate(entry, DubLanguage!, cancellationToken).ConfigureAwait(false);
        return dub is { IsPending: true } ? null : dub;
    }

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

    // Nested types

    private sealed class TimelineState(Moment startAt)
    {
        public Moment StartAt { get; } = startAt;
        public Moment? LastEntryEnd { get; set; }
        public TimeSpan SkippedDuration { get; set; }
        public TimeSpan NotBefore { get; set; }
    }

    private sealed record OpenedAudio(AudioSource Source, ReplayDub? Dub);

    // One entry's audio, read a frame ahead so the muxer can pick whichever frame plays first
    private sealed class EntryStream(
        ChatEntry entry,
        int streamIndex,
        TimeSpan playsAt,
        TimeSpan skipTo,
        double speed,
        Task<OpenedAudio?> openTask
        ) : IAsyncDisposable
    {
        private OpenedAudio? _opened;
        private IAsyncEnumerator<AudioFrame>? _frames;
        private TimeSpan? _tailCutoff;
        private int _rawFrameIndex;

        public ChatEntry Entry { get; } = entry;
        public int StreamIndex { get; } = streamIndex;
        public TimeSpan PlaysAt { get; } = playsAt;
        public TimeSpan SkipTo { get; } = skipTo;
        public bool IsLiveDub { get; init; }
        public AudioFrame Frame { get; private set; } = null!;
        public int FrameCount { get; private set; }
        // The raw duration streamed - the last frame's end offset, before any speed-up drops
        public TimeSpan StreamedDuration { get; private set; }
        public TimeSpan Deadline => ReplayTimeline.Deadline(PlaysAt, Frame.Offset, speed);

        public async ValueTask DisposeAsync()
        {
            if (_frames != null)
                await _frames.DisposeSilentlyAsync().ConfigureAwait(false);
            // An entry that was lined up but never started still owns whatever its download opened
            var opened = _opened ?? (await openTask.ResultAwait(false)).ValueOrDefault;
            // A live dub's source is shared with the synthesis that stores it, so only ours are disposed
            if (opened?.Dub?.Live == null)
                opened?.Source.Dispose();
        }

        public async Task<OpenedAudio?> Open(CancellationToken cancellationToken)
        {
            _opened = await openTask.ConfigureAwait(false);
            if (_opened is not { } opened)
                return null;

            // Only the original has the VAD's trailing silence to cut: a dub ends where its speech does
            if (opened.Dub == null)
                _tailCutoff = ReplayTimeline.TailCutoff(Entry, SkipTo, Constants.Audio.ReplayTailMargin);
            _frames = opened.Source.GetFrames(cancellationToken).GetAsyncEnumerator(cancellationToken);
            return opened;
        }

        public async Task<bool> MoveNext()
        {
            while (await _frames!.MoveNextAsync().ConfigureAwait(false)) {
                var frame = _frames.Current;
                if (frame.Offset >= _tailCutoff)
                    return false;

                StreamedDuration = frame.Offset + frame.Duration;
                if (!ReplayTimeline.MustKeepFrame(_rawFrameIndex++, speed))
                    continue; // Dropped for the speed-up

                Frame = frame;
                FrameCount++;
                return true;
            }
            return false;
        }
    }
}
