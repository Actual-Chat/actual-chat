using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.MediaPlayback;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class ChatReplayPlayer : ChatPlayer
{
    private static readonly TimeSpan ReplayClockPollPeriod = TimeSpan.FromMilliseconds(20);

    // Identifies the replay currently being played by this (reused) player.
    // ReplaySubHeader trusts the playback position only once this matches the
    // active ReplayState — otherwise the player is still winding down a previous run.
    public Moment CurrentStartAt { get; private set; }
    public TimeSpan CurrentRewindOffset { get; private set; }

    public ChatReplayPlayer(AppUIHub hub, ChatId chatId)
        : base(hub, chatId)
        => PlayerKind = ChatPlayerKind.Replaying;

    public override void Pause()
    {
        _ = Playback.Pause(CancellationToken.None);
        if (ChatAudioUI.ReplayState.Value is { } rs && rs.ChatId == ChatId)
            ChatAudioUI.TryReleaseAudioFocus();
    }

    public override async Task Resume()
    {
        var replayState = ChatAudioUI.ReplayState.Value;
        if (replayState is null || replayState.ChatId != ChatId) {
            Log.LogInformation("Can't resume replay. State: '{State}', ChatId: '{ChatId}'", replayState, ChatId);
            return;
        }

        if (!await ChatAudioUI.TryAcquireAudioFocusForResume(this).ConfigureAwait(false))
            return;

        _ = Playback.Resume(default);
    }

    protected override async Task Play(
        Playback playback,
        Moment startAt, // Server time
        CancellationToken cancellationToken)
    {
        var tracer = ChatAudioUI.ReplayTracer;
        tracer.Point("Player: Play started");
        var chat = await GetChat(cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return;

        tracer.Point("Player: chat loaded");
        var rewindOffset = TimeSpan.Zero;
        var speed = 1.0d;
        // Read rewindOffset and speed from ReplayState (set by ChatAudioUI.StartReplay)
        var replayState = ChatAudioUI.ReplayState.Value;
        if (replayState is { ChatId: var rsChatId } && rsChatId == ChatId) {
            speed = replayState.Speed;
            rewindOffset = replayState.RewindOffset;
        }
        CurrentStartAt = startAt;
        CurrentRewindOffset = rewindOffset;

        DebugLog?.LogDebug(
            "Replaying in #{ChatId} from {StartAt}, rewindOffset={RewindOffset}, speed={Speed}",
            ChatId, startAt, rewindOffset, speed);
        Operation = $"replaying in \"{chat.Title}\"";
        var streamProcessor = new ReplayStreamProcessor(
            Hub.Services, Session, ChatId, startAt, rewindOffset, speed,
            cancellationToken.CreateLinkedTokenSource()) {
            DubLanguageProvider = ct => Hub.TranslationUI.GetDubLanguage(ChatId, ct),
            Tracer = tracer,
        };
        await using var _ = streamProcessor.ConfigureAwait(false);

        // The clock starts with the first StreamStart rather than before the RPC call: the tracks'
        // PlaysAt are relative to the first one, so the call's latency must not count as played time
        var clockBase = CpuTimestamp.Now;
        ReplayClock? clock = null;
        var clockTracks = new ConcurrentDictionary<Symbol, (ReplayClock Clock, ReplayClock.Track Track)>();
        var trackTasks = new ConcurrentBag<Task>();
        // Raised from the demuxer's single loop, so a plain dictionary is enough
        var lastTrackTasks = new Dictionary<AuthorId, Task>();
        streamProcessor.StreamStarted += (info, playsAt, frames) => {
            var trackTracer = ReplayStreamProcessor.GetTrackTracer(tracer, info);
            if (clock == null) {
                clock = new ReplayClock(clockBase.Elapsed, ReplayClock.DefaultMaxExtrapolation);
                trackTracer.Point("StreamStart: replay clock started");
            }
            trackTracer.Point($"StreamStart: author {info.AuthorId}, playsAt {playsAt.TotalSeconds:F3}s");
            var trackTask = OnStreamStarted(
                playback, info, playsAt, frames, speed,
                clock, clockBase, clockTracks,
                lastTrackTasks.GetValueOrDefault(info.AuthorId), trackTracer, cancellationToken);
            lastTrackTasks[info.AuthorId] = trackTask;
            trackTasks.Add(trackTask);
        };
        Action<TrackInfo, PlayerState> onTrackPlayingChanged = (trackInfo, state) => {
            if (clockTracks.TryGetValue(trackInfo.TrackId, out var clockTrack))
                clockTrack.Clock.ReportProgress(
                    clockTrack.Track, state.PlayingAt / speed, state.IsPaused, clockBase.Elapsed);
        };

        playback.OnTrackPlayingChanged += onTrackPlayingChanged;

        try {
            // Wait for server to finish streaming all entries
            await streamProcessor.Run().ConfigureAwait(false);

            // Wait for all tracks to finish playing (but respect cancellation)
            await Task.WhenAll(trackTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally {
            playback.OnTrackPlayingChanged -= onTrackPlayingChanged;
        }
    }

    // Private methods

    private Task OnStreamStarted(
        Playback playback,
        LiveAudioStreamInfo streamInfo,
        TimeSpan playsAt,
        IAsyncEnumerable<AudioFrame> audioFrames,
        double speed,
        ReplayClock clock,
        CpuTimestamp clockBase,
        ConcurrentDictionary<Symbol, (ReplayClock Clock, ReplayClock.Track Track)> clockTracks,
        Task? previousAuthorTrackTask,
        Tracer tracer,
        CancellationToken cancellationToken)
        => BackgroundTask.Run(async () => {
            ReplayClock.Track? clockTrack = null;
            Symbol trackId = default;
            try {
                await WaitForClock(clock, clockBase, playsAt, cancellationToken).ConfigureAwait(false);
                // From here on the track holds the clock until its audio catches up with it
                clockTrack = clock.StartTrack(playsAt, clockBase.Elapsed);
                tracer.Point($"due, replay clock at {clock.GetPosition(clockBase.Elapsed).TotalSeconds:F3}s");
                // With the clock following the audio, only overlapping recordings of one speaker
                // still wait here - and one speaker must never talk over themselves
                if (previousAuthorTrackTask is { IsCompleted: false }) {
                    await previousAuthorTrackTask.SilentAwait(false);
                    tracer.Point("the author's previous track ended");
                }

                if (!await CanContinuePlayback(cancellationToken).ConfigureAwait(false))
                    return;

                // Create AudioSource from the stream frames
                var skipTo = TimeSpan.Zero; // Server already handles skip
                var audioSource = CreateAudioSource(streamInfo, audioFrames, skipTo, cancellationToken);

                // Get chat and author info for track metadata
                var chat = await Hub.Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
                if (chat == null)
                    return;

                var author = await Hub.Authors
                    .Get(Session, ChatId, streamInfo.AuthorId, cancellationToken)
                    .ConfigureAwait(false);

                // Look up ChatEntry from EntryId if available
                ChatEntry? audioEntry = null;
                if (streamInfo.EntryId is { } entryId) {
                    var entryReader = Hub.NewEntryReader(ChatId);
                    audioEntry = await entryReader.Get(entryId.LocalId, cancellationToken).ConfigureAwait(false);
                }

                var targetBufferSize = await Hub.ChatAudioUI
                    .GetPlaybackTargetBufferSize(ChatId, cancellationToken)
                    .ConfigureAwait(false);
                tracer.Point($"metadata loaded, {FormatAudioExtent(audioEntry)}");
                var trackInfo = audioEntry != null
                    ? new ChatAudioTrackInfo(audioEntry, chat, author!) {
                        RecordedAt = streamInfo.BeginsAt,
                        SourceRecordedAt = streamInfo.BeginsAt,
                        Speed = speed,
                        TargetBufferSize = targetBufferSize,
                        StreamId = streamInfo.StreamId,
                        Tracer = tracer,
                    }
                    : new ChatAudioTrackInfo(ChatId, streamInfo.EntryId, chat, author) {
                        RecordedAt = streamInfo.BeginsAt,
                        SourceRecordedAt = streamInfo.BeginsAt,
                        Speed = speed,
                        TargetBufferSize = targetBufferSize,
                        StreamId = streamInfo.StreamId,
                        Tracer = tracer,
                    };
                trackId = trackInfo.TrackId;
                clockTracks[trackId] = (clock, clockTrack);
                var process = playback.Play(trackInfo, audioSource, cancellationToken);
                await process.WhenCompleted.ConfigureAwait(false);
                tracer.Point("playback completed");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // Expected
            }
            catch (Exception e) {
                Log.LogWarning(e, "Error processing replay stream #{StreamId}", streamInfo.StreamId);
            }
            finally {
                if (!trackId.IsEmpty)
                    clockTracks.TryRemove(trackId, out _);
                if (clockTrack != null)
                    clock.EndTrack(clockTrack, clockBase.Elapsed);
            }
        }, CancellationToken.None);

    private async Task WaitForClock(
        ReplayClock clock,
        CpuTimestamp clockBase,
        TimeSpan playsAt,
        CancellationToken cancellationToken)
    {
        while (true) {
            await Playback.IsPaused.Computed
                .When(x => !x, cancellationToken)
                .ConfigureAwait(false);

            var remaining = playsAt - clock.GetPosition(clockBase.Elapsed);
            if (remaining <= TimeSpan.Zero)
                return;

            // The clock follows the playing tracks' progress, so it's polled rather than waited on
            await Clocks.CpuClock
                .Delay(TimeSpanExt.Min(remaining, ReplayClockPollPeriod), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private AudioSource CreateAudioSource(
        LiveAudioStreamInfo streamInfo,
        IAsyncEnumerable<AudioFrame> audioFrames,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var format = streamInfo.Format ?? AudioSource.DefaultFormat;
        var frameStream = audioFrames
            .SkipWhile(f => f.Offset < skipTo)
            .Select(f => new AudioFrame {
                Data = f.Data,
                Offset = f.Offset - skipTo,
                Duration = f.Duration,
            });

        return new AudioSource(
            streamInfo.BeginsAt,
            format,
            frameStream,
            TimeSpan.Zero,
            Log,
            cancellationToken);
    }

    private static string FormatAudioExtent(ChatEntry? entry)
    {
        // The replay timeline is built from the entry's first/last word, while the blob it plays
        // starts earlier (lead) and runs past the last word (tail)
        if (entry is not { Audio: { EndsAt: { } audioEndsAt } audio })
            return "no audio extent";

        var entryEndsAt = entry.GetEndsAt();
        var lead = entry.BeginsAt - audio.BeginsAt;
        var speech = entryEndsAt - entry.BeginsAt;
        var tail = audioEndsAt - entryEndsAt;
        return $"lead {lead.TotalSeconds:F3}s, speech {speech.TotalSeconds:F3}s, tail {tail.TotalSeconds:F3}s";
    }
}
