using ActualChat.Audio;
using ActualChat.Concurrency;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Android.Media;
using AudioFormat = Android.Media.AudioFormat;
using AudioSource = ActualChat.Audio.AudioSource;
using Encoding = Android.Media.Encoding;

namespace ActualChat.App.Maui.Audio;

internal sealed class AndroidAudioPlaybackEngine(
    string id,
    TrackInfo info,
    IMediaSource source,
    IAudioPlayerBackend playerBackend,
    IServiceProvider services
    ) : ProcessorBase, IAudioPlaybackEngine
{
    private const long LagReportIntervalMs = 500;
    private const int PositionReportPeriodMs = 200;
    private const int MinDrainPollMs = 20;
    private static readonly TimeSpan DrainStallTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRouteWait = TimeSpan.FromSeconds(3);

    private readonly DurationTargetingFrameBuffer<AudioFrame> _frames = new(
        static frame => frame.Offset,
        static frame => frame.Duration);

    // The samples last written to a track, kept for a usage switch to replay
    private readonly float[] _tail = new float[Constants.Audio.PlaybackSampleRate * 3];

    private CancellationTokenSource? _pauseEndTokenSource;
    private AudioTrack? _audioTrack;
    private Task? _decodeAndFeedTask;
    private Task? _positionWatchTask;
    private GCHandle _audioTrackHandle;

    private int _remainingPreSkip;
    private float _gain = 1f;
    private int _fedSampleCount;
    private int _lastPlayedSampleCount;
    private int _isEnded;
    private long _nextLagReportAtTicks;
    private long _maxPresentationLagTicks = long.MinValue;
    private int _underrunCountBeforeDrain = -1;
    private CarAudioRoute _route = CarAudioRoute.Default;
    private AudioUsageKind _usage;
    private int _trackStartSampleCount;
    private Task? _routeTask;
    private CpuTimestamp _routeRequestedAt;

    private AudioFocusUI AudioFocusUI => field ??= services.GetRequiredService<AudioFocusUI>();
    private ChatAudioUI ChatAudioUI => field ??= services.GetRequiredService<ChatAudioUI>();
    private IAudioCodec AudioCodec => field ??= services.GetRequiredService<IAudioCodec>();
    private MomentClockSet Clocks => field ??= services.GetRequiredService<MomentClockSet>();
    private ILogger Log => field ??= services.LogFor<AndroidAudioPlaybackEngine>();

    public async Task Play(CancellationToken cancellationToken)
    {
        Log.LogDebug("Play called: id={Id}", info.TrackId);
        // Guards against a second Play() call; Volatile.Read pairs with the Volatile.Write below.
        if (Volatile.Read(ref _decodeAndFeedTask) is not null)
            return;

        // Before the track exists, not after: Android hands the communication route back to the
        // earpiece once it decides the focus holder is idle, and a wake takes focus seconds before
        // its first frames arrive - a track built then stays on the earpiece for its whole life.
        // The route lookup leads, so the built-in branch never runs EnsureOutputRoute: that one
        // picks the best communication device, and a Bluetooth pick there opens SCO - the very
        // virtual call routing playback to the phone exists to avoid.
        var route = await ChatAudioUI.GetCarAudioRoute(cancellationToken).ConfigureAwait(false);
        if (route.Output == AudioEndpoint.Builtin)
            await AudioFocusUI.EnsureBuiltinSpeakerRoute(cancellationToken).ConfigureAwait(false);
        else
            await AudioFocusUI.EnsureOutputRoute(cancellationToken).ConfigureAwait(false);

        var audioSource = (AudioSource)source;
        _remainingPreSkip = audioSource.Format.PreSkip;
        _frames.SetTargetDuration(GetEncodedBufferDuration(info.TargetBufferSize));

        _route = route;
        _usage = GetUsage();
        _gain = route.Output == AudioEndpoint.External && !route.UseHandsFreeLink
            ? Constants.Audio.ProjectionMediaGain
            : 1f;
        Log.LogInformation(
            "Play: id={Id}, car route {Route}, usage {Usage}, comm focus {IsCommunicationFocus}, gain {Gain}",
            info.TrackId, route, _usage, AudioFocusUI.IsCommunicationFocus, _gain);
        var audioTrack = CreateAudioTrack(_usage);

        lock (Lock) {
            // Publishing state for the background tasks and for callers of Pause/Resume/End, none of
            // which take this lock - each Volatile.Write pairs with a Volatile.Read at its call site.
            Volatile.Write(ref _lastPlayedSampleCount, 0);
            _pauseEndTokenSource = null;

            Volatile.Write(ref _audioTrack, audioTrack);
            _audioTrackHandle = GCHandle.Alloc(audioTrack, GCHandleType.Normal);
            Volatile.Write(ref _decodeAndFeedTask, BackgroundTask.Run(DecodeAndFeed, CancellationToken.None));
            Volatile.Write(ref _positionWatchTask, BackgroundTask.Run(WatchPlaybackPosition, CancellationToken.None));
        }

        audioTrack.Play();
        Log.LogInformation("Play: id={Id} routed to {Device}; {AudioState}",
            info.TrackId, AndroidAudioRouteLog.Describe(audioTrack.RoutedDevice), AndroidAudioRouteLog.DescribeState());
        // Disabled, logging only: a never-unregistered listener, suspected of the self-recursive JNI Invoker
        // stack overflow described at WatchPlaybackPosition (Sentry ACTUAL-CHAT-UI-101D).
        // audioTrack.RoutingChanged += (_, _) => {
        //     string device;
        //     try {
        //         device = AndroidAudioRouteLog.Describe(audioTrack.RoutedDevice);
        //     }
        //     catch {
        //         return;
        //     }
        //
        //     _ = BackgroundTask.Run(() => {
        //         Log.LogInformation("Play: id={Id} rerouted to {Device}; {AudioState}",
        //             info.TrackId, device, AndroidAudioRouteLog.DescribeState());
        //         return Task.CompletedTask;
        //     });
        // };
        NotifyPlaying(0); // Initial report that we're ready to play
    }

    protected override async Task DisposeAsyncCore()
    {
        // This method starts inside lock (Lock).
        // Both background tasks observe StopToken, which ProcessorBase.DisposeAsync cancels before
        // calling this method, so awaiting them here cannot deadlock. Letting them fully stop before
        // releasing the track is what guarantees no callback ever runs against a released track.
        // Volatile pairs with the writes in Play(), since neither task's owning field is read under a lock.
        var decodeAndFeedTask = Volatile.Read(ref _decodeAndFeedTask);
        if (decodeAndFeedTask is not null) {
            await decodeAndFeedTask.SilentAwait();
            Volatile.Write(ref _decodeAndFeedTask, null);
        }
        var positionWatchTask = Volatile.Read(ref _positionWatchTask);
        if (positionWatchTask is not null) {
            await positionWatchTask.SilentAwait();
            Volatile.Write(ref _positionWatchTask, null);
        }

        lock (Lock) { // We must re-lock after await
            var audioTrack = Volatile.Read(ref _audioTrack);
            if (audioTrack.IsValid()) {
                ReportTrackHealth(audioTrack);
                try {
                    if (audioTrack.PlayState is PlayState.Playing or PlayState.Paused)
                        audioTrack.Stop();
                }
                catch { /* Ignore */ }
                try { audioTrack.Release(); } catch { /* Ignore */ }
                audioTrack.DisposeSilently();
            }
            Volatile.Write(ref _audioTrack, null);

            if (_audioTrackHandle.IsAllocated)
                _audioTrackHandle.Free();
        }
    }

    public Task Pause(CancellationToken cancellationToken)
    {
        // Cross-thread read of the track Play() publishes under Lock.
        var audioTrack = Volatile.Read(ref _audioTrack);
        if (audioTrack is null)
            throw StandardError.AudioPlayer.PlayingStateExpected(GetType());

        audioTrack.Pause();
        // Enter paused state: create a gate CTS that will be canceled on Resume()
        _pauseEndTokenSource?.CancelAndDisposeSilently();
        _pauseEndTokenSource = new CancellationTokenSource();
        NotifyPlaying(GetPlayedSampleCount());
        return Task.CompletedTask;
    }

    public Task Resume(CancellationToken cancellationToken)
    {
        // Cross-thread read of the track Play() publishes under Lock.
        var audioTrack = Volatile.Read(ref _audioTrack);
        if (audioTrack is null)
            throw StandardError.AudioPlayer.PlayingStateExpected(GetType());

        _pauseEndTokenSource?.CancelAndDisposeSilently();
        _pauseEndTokenSource = null;
        audioTrack.Play();
        NotifyPlaying(GetPlayedSampleCount());
        return Task.CompletedTask;
    }

    public Task End(bool mustAbort, CancellationToken cancellationToken)
    {
        var enqueuedSampleCount = _frames.Count;
        Log.LogDebug(
            "End called: id={Id} abort={Abort} enqueued={Frames} fed={FeedSampleCount}",
            info.TrackId, mustAbort, enqueuedSampleCount, Volatile.Read(ref _fedSampleCount));

        // Cross-thread read of the track Play() publishes under Lock.
        var audioTrack = Volatile.Read(ref _audioTrack).IfValid();
        if (mustAbort) {
            try {
                if (audioTrack is not null && audioTrack.PlayState is PlayState.Playing or PlayState.Paused)
                    audioTrack.Stop();
            }
            catch {
                // Ignore
            }
            _frames.Complete();
            StopTokenSource.CancelAndDisposeSilently();
            _pauseEndTokenSource.CancelAndDisposeSilently();
            NotifyEnded(null);
        }
        else {
            _frames.Complete();
            try {
                if (audioTrack?.PlayState is PlayState.Stopped)
                    audioTrack.Play(); // Start playback if stopped (not paused)
            }
            catch {
                // Ignore
            }
        }

        return Task.CompletedTask;
    }

    public ValueTask PushFrame(AudioFrame frame, CancellationToken cancellationToken)
    {
        var data = frame.Data;
        if (data.Length == 0)
            return ValueTask.CompletedTask;

        _frames.Push(frame);
        return ValueTask.CompletedTask;
    }

    // Private methods

    private async Task DecodeAndFeed()
    {
        // Cross-thread read of the track Play() publishes under Lock.
        var audioTrack = Volatile.Read(ref _audioTrack)!;
        var cancellationToken = StopToken;
        var audioData = new float[Constants.Audio.PcmFrameLength];
        try {
            var frames = _frames.ReadAllAsync(cancellationToken);
            await foreach (var pcmOwner in AudioCodec.Decode(frames, cancellationToken).ConfigureAwait(false)) {
                using var _ = pcmOwner;
                var pcm = pcmOwner.Memory;
                var samples = pcm.Length;
                if (samples <= 0)
                    continue;

                // Apply Opus pre-skip: drop the first _remainingPreSkip samples from decoder output
                if (_remainingPreSkip > 0)
                    if (_remainingPreSkip >= samples) {
                        _remainingPreSkip -= samples;
                        continue; // Entire buffer skipped
                    }

                var skip = Math.Min(_remainingPreSkip, samples);
                var playSamples = samples - skip;
                if (playSamples > 0) {
                    audioTrack = await SwitchUsageIfNeeded(audioTrack, cancellationToken).ConfigureAwait(false);
                    // If playback is paused, wait until it is resumed before feeding more data
                    await WhenUnpaused(cancellationToken).ConfigureAwait(false);

                    if (!CanContinuePlaying(out var _)) {
                        _frames.Complete();
                        Log.LogDebug($"AudioTrack became dead or stopped, terminating {nameof(DecodeAndFeed)}");
                        break;
                    }

                    pcm.Span.CopyTo(audioData.AsSpan(0, pcm.Length));
                    if (_gain != 1f)
                        AudioExt.Amplify(audioData.AsSpan(skip, playSamples), _gain);
                    var written = await audioTrack
                        .WriteAsync(audioData, skip, playSamples, WriteMode.Blocking)
                        .ConfigureAwait(false);
                    if (written < 0) {
                        // An AudioTrack error code - ERROR_DEAD_OBJECT after an audioserver restart
                        // or a route teardown. The track still reports Initialized/Playing, so
                        // CanContinuePlaying never trips and later writes discard audio in silence.
                        _frames.Complete();
                        throw StandardError.External($"AudioTrack.Write failed with {written}.");
                    }

                    // Interlocked, not Volatile.Write: this publishes a delta, not the latest value.
                    if (written > 0) {
                        CopyToTail(audioData.AsSpan(skip, written), Volatile.Read(ref _fedSampleCount));
                        Interlocked.Add(ref _fedSampleCount, written);
                    }
                }

                _remainingPreSkip -= skip;
                if (_remainingPreSkip < 0)
                    _remainingPreSkip = 0;
            }
            // A drained track runs dry by design, so the underruns from here on aren't playback faults
            Volatile.Write(ref _underrunCountBeforeDrain, audioTrack.UnderrunCount);
            await WhenPlaybackDrained(cancellationToken).ConfigureAwait(false);
            await End(true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "DecodeAndFeed failed");
            NotifyEnded(e.Message);
        }
    }

    private async Task<AudioTrack> SwitchUsageIfNeeded(AudioTrack audioTrack, CancellationToken cancellationToken)
    {
        // A track's usage is fixed at construction, and the mic opening or closing mid-utterance
        // changes the right one: Media beside an open mic echoes, VoiceCommunication beside a
        // closed one drops to the earpiece. Playing out the old track first isn't an option: a
        // Media track holds over a second, all of it heard twice. So it stops at once, and the
        // part it hadn't played yet goes into the new track from the copy of the written samples.
        var usage = GetUsage();
        if (usage == _usage) {
            _routeTask = null; // The mic flipped back before the route landed
            return audioTrack;
        }

        if (_routeTask is null)
            Log.LogInformation(
                "Play: id={Id} switching usage {OldUsage} -> {Usage}, comm focus {IsCommunicationFocus}, call {IsCall}",
                info.TrackId, _usage, usage, AudioFocusUI.IsCommunicationFocus, AudioFocusUI.IsCallActive);
        if (usage == AudioUsageKind.VoiceCommunication && _route.Output != AudioEndpoint.Builtin) {
            // The communication device can take a second to land, and a track built before it does
            // stays on the earpiece. The old track goes on being fed meanwhile, or it runs dry.
            if (_routeTask is null) {
                _routeTask = AudioFocusUI.EnsureOutputRoute(cancellationToken);
                _routeRequestedAt = CpuTimestamp.Now;
            }
            if (!IsCommunicationRouteReady())
                return audioTrack;

            await _routeTask.SilentAwait(false);
            _routeTask = null;
        }
        _usage = usage;

        var unplayedSampleCount = StopAndGetUnplayedSampleCount(audioTrack);
        var nextAudioTrack = CreateAudioTrack(usage);
        // Only this loop adds to it; Volatile matches the Interlocked writes
        var fedSampleCount = Volatile.Read(ref _fedSampleCount);
        lock (Lock) {
            // Publication for the readers outside the lock, as in Play()
            Volatile.Write(ref _trackStartSampleCount, fedSampleCount - unplayedSampleCount);
            Volatile.Write(ref _audioTrack, nextAudioTrack);
            _audioTrackHandle.Free();
            _audioTrackHandle = GCHandle.Alloc(nextAudioTrack, GCHandleType.Normal);
        }
        nextAudioTrack.Play();
        if (_pauseEndTokenSource is not null)
            nextAudioTrack.Pause();
        Log.LogInformation(
            "Play: id={Id} switched to {Usage}, replaying {Replayed}ms, routed to {Device}; {AudioState}",
            info.TrackId, usage, unplayedSampleCount * 1000L / Constants.Audio.PlaybackSampleRate,
            AndroidAudioRouteLog.Describe(nextAudioTrack.RoutedDevice), AndroidAudioRouteLog.DescribeState());

        try { audioTrack.Release(); } catch { /* Ignore */ }
        audioTrack.DisposeSilently();
        await WriteUnplayed(nextAudioTrack, fedSampleCount, unplayedSampleCount).ConfigureAwait(false);
        return nextAudioTrack;
    }

    private bool IsCommunicationRouteReady()
    {
        // EnsureOutputRoute gives up after a second, and a Samsung was seen to take the speaker only
        // once the mic it was opened for began recording - so the device is checked, not the task
        if (_routeTask is not { IsCompleted: true })
            return false;
        if (AudioFocusUI.IsCallActive || !OperatingSystem.IsAndroidVersionAtLeast(31))
            return true;

        return CommunicationDeviceListener.LastDevice?.Type != AudioDeviceType.BuiltinEarpiece
            || _routeRequestedAt.Elapsed >= MaxRouteWait;
    }

    private int StopAndGetUnplayedSampleCount(AudioTrack audioTrack)
    {
        // Only the feed loop writes either, and this runs on it; Volatile matches the other accesses
        var trackFedSampleCount = Volatile.Read(ref _fedSampleCount) - Volatile.Read(ref _trackStartSampleCount);
        var playedSampleCount = trackFedSampleCount;
        try {
            audioTrack.Pause();
            playedSampleCount = audioTrack.PlaybackHeadPosition;
            audioTrack.Stop();
        }
        catch {
            // A dead track has nothing left to hand over
        }
        return Math.Clamp(trackFedSampleCount - playedSampleCount, 0, Math.Min(trackFedSampleCount, _tail.Length));
    }

    private async Task WriteUnplayed(AudioTrack audioTrack, int fedSampleCount, int unplayedSampleCount)
    {
        // The tail is a ring indexed by the absolute sample count, so the unplayed part may wrap
        var start = fedSampleCount - unplayedSampleCount;
        while (unplayedSampleCount > 0) {
            var offset = start % _tail.Length;
            var length = Math.Min(unplayedSampleCount, _tail.Length - offset);
            var written = await audioTrack.WriteAsync(_tail, offset, length, WriteMode.Blocking).ConfigureAwait(false);
            if (written <= 0)
                return;

            start += written;
            unplayedSampleCount -= written;
        }
    }

    private void CopyToTail(ReadOnlySpan<float> samples, int fedSampleCount)
    {
        var offset = fedSampleCount % _tail.Length;
        var firstLength = Math.Min(samples.Length, _tail.Length - offset);
        samples[..firstLength].CopyTo(_tail.AsSpan(offset));
        samples[firstLength..].CopyTo(_tail);
    }

    private AudioUsageKind GetUsage()
        => _route.UseCommunicationPlayback(AudioFocusUI)
            ? AudioUsageKind.VoiceCommunication
            : AudioUsageKind.Media;

    private AudioTrack CreateAudioTrack(AudioUsageKind usage)
    {
        var sampleRate = Constants.Audio.PlaybackSampleRate;
        var channelOut = ChannelOut.Mono;
        var encoding = Encoding.PcmFloat;

        var minBufferBytes = AudioTrack.GetMinBufferSize(sampleRate, channelOut, encoding);
        if (minBufferBytes <= 0)
            throw new InvalidOperationException($"AudioTrack min buffer size invalid: {minBufferBytes}");

        // Minimum buffer size to keep Feed blocked on track.WriteAsync
        var bufferBytes = Math.Max(minBufferBytes, Constants.Audio.PcmFrameLength * 4);
        try {
            var attributes = new AudioAttributes.Builder()
                .SetUsage(usage)!
                .SetContentType(AudioContentType.Speech)!
                .Build();

            var audioFormat = new AudioFormat.Builder()
                .SetEncoding(encoding)!
                .SetSampleRate(sampleRate)!
                .SetChannelMask(channelOut)
                .Build();

            return new AudioTrack.Builder()
                .SetAudioAttributes(attributes!)
                .SetAudioFormat(audioFormat!)
                .SetBufferSizeInBytes(bufferBytes)
                .SetTransferMode(AudioTrackMode.Stream)
                .SetSessionId(AudioManager.AudioSessionIdGenerate)
                .Build();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize AudioTrack");
            throw;
        }
    }

    private void NotifyPlaying(int playedSampleCount)
    {
        var played = (double)playedSampleCount / Constants.Audio.PlaybackSampleRate;
        // Cross-thread read of the count DecodeAndFeed accumulates via Interlocked.Add.
        var fedSampleCount = Volatile.Read(ref _fedSampleCount);
        var buffered = TimeSpan.FromSeconds(
            (double)(fedSampleCount - playedSampleCount) / Constants.Audio.PlaybackSampleRate);
        var isBufferLow = buffered < Constants.Audio.LowPlaybackBufferDuration;
        // Cross-thread read of the track Play() publishes under Lock.
        var audioTrack = Volatile.Read(ref _audioTrack);
        var isPaused = !audioTrack.IsValid() || audioTrack.PlayState is PlayState.Paused or PlayState.Stopped;
        try {
            playerBackend.OnPlaying(played, isPaused, isBufferLow);
        }
        catch {
            // Don't propagate reporting errors
        }
        if (!isPaused && playedSampleCount > 0)
            TryReportPresentationLag(played);
    }

    private void TryReportPresentationLag(double playheadOffsetSeconds)
    {
        var nowTicks = Environment.TickCount64;
        if (nowTicks < Interlocked.Read(ref _nextLagReportAtTicks))
            return;

        Interlocked.Exchange(ref _nextLagReportAtTicks, nowTicks + LagReportIntervalMs);

        var anchor = info.SourceRecordedAt != default ? info.SourceRecordedAt : info.RecordedAt;
        if (anchor == default)
            return;

        var lag = Clocks.ServerClock.Now - anchor
            - TimeSpan.FromSeconds(playheadOffsetSeconds)
            + Constants.Audio.AudioEnginePlaybackLatency;
        InterlockedExt.ExchangeIfGreater(ref _maxPresentationLagTicks, lag.Ticks);
        try {
            playerBackend.OnPresentationLag(lag);
        }
        catch {
            // Don't propagate reporting errors
        }
    }

    private void NotifyEnded(string? message)
    {
        if (Interlocked.Exchange(ref _isEnded, 1) != 0)
            return;

        try {
            playerBackend.OnEnded(message);
        }
        catch {
            // Ignore
        }
    }

    private bool CanContinuePlaying([NotNullWhen(true)] out AudioTrack? audioTrack)
    {
        // Cross-thread read of the track Play() publishes under Lock.
        audioTrack = Volatile.Read(ref _audioTrack);
        try {
            if (!audioTrack.IsValid())
                return false;
            if (audioTrack.State != AudioTrackState.Initialized)
                return false;

            return audioTrack.PlayState != PlayState.Stopped;
        }
        catch {
            return false;
        }
    }

    private int GetPlayedSampleCount()
    {
        // Cross-thread reads of counts DecodeAndFeed/GetPlayedSampleCount publish via Interlocked.
        var fedSampleCount = Volatile.Read(ref _fedSampleCount);
        var lastPlayedSampleCount = Volatile.Read(ref _lastPlayedSampleCount);
        if (!CanContinuePlaying(out var audioTrack))
            return Math.Max(fedSampleCount, lastPlayedSampleCount); // Pretend we played everything

        try {
            // Cross-thread read of the start SwitchUsageIfNeeded publishes with its track. A track it
            // swapped in starts its head from zero; a mismatched start/track pair is absorbed by the clamps.
            var playedSampleCount = (Volatile.Read(ref _trackStartSampleCount) + audioTrack.PlaybackHeadPosition)
                .Clamp(0, fedSampleCount); // Can't go beyond end
            playedSampleCount = Math.Max(playedSampleCount, lastPlayedSampleCount); // Can't decrease
            Interlocked.Exchange(ref _lastPlayedSampleCount, playedSampleCount);
            return playedSampleCount;
        }
        catch {
            return Math.Max(fedSampleCount, lastPlayedSampleCount); // Pretend we played everything
        }
    }

    private async Task WhenUnpaused(CancellationToken cancellationToken)
    {
        var pauseEndTokenSource = _pauseEndTokenSource;
        if (pauseEndTokenSource is null)
            return;

        CancellationToken pauseEndToken;
        try {
            pauseEndToken = pauseEndTokenSource.Token;
        }
        catch (ObjectDisposedException) {
            // Resume() and End() cancel-and-dispose this source from the command loop, racing the
            // read above. A disposed source means the pause is already over, so returning is
            // right - and the alternative was this exception reaching DecodeAndFeed's catch and
            // ending a live track, on a path that runs every time focus churns.
            return;
        }

        await TaskExt.NeverEnding(pauseEndToken).SilentAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    // We poll PlaybackHeadPosition instead of registering an AudioTrack.IOnPlaybackPositionUpdateListener.
    // A native position-update callback fired while the listener's managed peer is being torn down resurrects
    // it as a self-recursive JNI Invoker and overflows the stack. Polling removes the callback entirely.
    private async Task WatchPlaybackPosition()
    {
        var cancellationToken = StopToken;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                NotifyPlaying(GetPlayedSampleCount());
                await Task.Delay(PositionReportPeriodMs, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e.IsCancellationOf(cancellationToken)) {
            // Expected on End/Dispose
        }
    }

    private async Task WhenPlaybackDrained(CancellationToken cancellationToken)
    {
        // A STREAM-mode track that underruns on its tail goes disabled: the head freezes short of
        // what was written and only another write would restart it. Waiting for the head to reach
        // the end then never returns, so a head that stops moving is treated as drained.
        var sampleRate = Constants.Audio.PlaybackSampleRate;
        var lastPlayedSampleCount = -1;
        var stalledAt = CpuTimestamp.Now;
        while (CanContinuePlaying(out var audioTrack)) {
            // Cross-thread read of the count DecodeAndFeed accumulates via Interlocked.Add.
            var fedSampleCount = Volatile.Read(ref _fedSampleCount);
            var playedSampleCount = GetPlayedSampleCount();
            var remaining = fedSampleCount - playedSampleCount;
            if (remaining <= 0)
                return;

            if (playedSampleCount != lastPlayedSampleCount || audioTrack.PlayState == PlayState.Paused) {
                lastPlayedSampleCount = playedSampleCount;
                stalledAt = CpuTimestamp.Now;
            }
            else if (stalledAt.Elapsed >= DrainStallTimeout) {
                Log.LogWarning(
                    "Playback head stalled at {Played}/{Fed} samples for {Elapsed}: id={Id}, ending the track",
                    playedSampleCount, fedSampleCount, stalledAt.Elapsed.ToShortString(), info.TrackId);
                return;
            }

            var delayMs = (int)Math.Clamp(remaining * 1000L / sampleRate, MinDrainPollMs, PositionReportPeriodMs);
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private void ReportTrackHealth(AudioTrack audioTrack)
    {
        // UnderrunCount is what AudioFlinger logs as "ACTIVE underrun"; the app can't read those lines,
        // and AudioTrackPlayer's own count never moves here, as this engine doesn't report starving
        try {
            var played = TimeSpan.FromSeconds(
                (double)Volatile.Read(ref _lastPlayedSampleCount) / Constants.Audio.PlaybackSampleRate);
            var maxLagTicks = Interlocked.Read(ref _maxPresentationLagTicks);
            var maxLag = maxLagTicks == long.MinValue ? "n/a" : TimeSpan.FromTicks(maxLagTicks).ToShortString();
            var underrunCount = audioTrack.UnderrunCount;
            var underrunCountBeforeDrain = Volatile.Read(ref _underrunCountBeforeDrain);
            // An aborted track never reached the drain, so all its underruns count as mid-track
            var drainUnderrunCount = underrunCountBeforeDrain < 0 ? 0 : underrunCount - underrunCountBeforeDrain;
            Log.LogInformation(
                "Track {Id} done: played {Played}, {UnderrunCount} underruns + {DrainUnderrunCount} on drain, "
                + "buffer {BufferFrames}/{CapacityFrames} frames, max lag {MaxLag}",
                info.TrackId, played.ToShortString(), underrunCount - drainUnderrunCount, drainUnderrunCount,
                audioTrack.BufferSizeInFrames, audioTrack.BufferCapacityInFrames, maxLag);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Couldn't read the health of track {Id}", info.TrackId);
        }
    }

    private static TimeSpan GetEncodedBufferDuration(TimeSpan targetBufferSize)
    {
        var encoded = targetBufferSize - Constants.Audio.DecodedBufferSize - Constants.Audio.AudioEnginePlaybackLatency;
        return TimeSpanExt.Max(encoded, Constants.Audio.MinEncodedBufferSize);
    }
}
