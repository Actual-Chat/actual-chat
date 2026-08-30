using ActualChat.Audio;
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

    private readonly DurationTargetingFrameBuffer<AudioFrame> _frames = new(
        static frame => frame.Offset,
        static frame => frame.Duration);

    private CancellationTokenSource? _pauseEndTokenSource;
    private AudioTrack? _audioTrack;
    private Task? _decodeAndFeedTask;
    private Task? _positionWatchTask;
    private GCHandle _audioTrackHandle;

    private int _remainingPreSkip;
    private int _fedSampleCount;
    private int _lastPlayedSampleCount;
    private int _isEnded;
    private long _nextLagReportAtTicks;

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

        // Configure AudioTrack for float32 PCM mono at playback sample rate
        var sampleRate = Constants.Audio.PlaybackSampleRate;
        var channelOut = ChannelOut.Mono;
        var encoding = Encoding.PcmFloat;

        var minBufferBytes = AudioTrack.GetMinBufferSize(sampleRate, channelOut, encoding);
        if (minBufferBytes <= 0)
            throw new InvalidOperationException($"AudioTrack min buffer size invalid: {minBufferBytes}");

        // Minimum buffer size to keep Feed blocked on track.WriteAsync
        var bufferBytes = Math.Max(minBufferBytes, Constants.Audio.PcmFrameLength * 4);
        AudioTrack audioTrack;
        try {
            // RemoteSubmix carries Media to the car but doesn't capture VOICE_COMMUNICATION, so that
            // usage is what pins playback to the phone. Outside a car it's right only while a comm
            // focus is held: a Media-usage recording focus leaves no comm route, i.e. the earpiece.
            var usage = route.Output switch {
                AudioEndpoint.External => AudioUsageKind.Media,
                AudioEndpoint.Builtin => AudioUsageKind.VoiceCommunication,
                _ => AudioFocusUI.IsCommunicationFocus
                    ? AudioUsageKind.VoiceCommunication
                    : AudioUsageKind.Media,
            };
            var attributes = new AudioAttributes.Builder()
                .SetUsage(usage)!
                .SetContentType(AudioContentType.Speech)!
                .Build();

            var audioFormat = new AudioFormat.Builder()
                .SetEncoding(encoding)!
                .SetSampleRate(sampleRate)!
                .SetChannelMask(channelOut)
                .Build();

            audioTrack = new AudioTrack.Builder()
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
                    // If playback is paused, wait until it is resumed before feeding more data
                    await WhenUnpaused(cancellationToken).ConfigureAwait(false);

                    if (!CanContinuePlaying(out var _)) {
                        _frames.Complete();
                        Log.LogDebug($"AudioTrack became dead or stopped, terminating {nameof(DecodeAndFeed)}");
                        break;
                    }

                    pcm.Span.CopyTo(audioData.AsSpan(0, pcm.Length));
                    var written = await audioTrack
                        .WriteAsync(audioData, skip, playSamples, WriteMode.Blocking)
                        .ConfigureAwait(false);
                    // Interlocked, not Volatile.Write: this publishes a delta, not the latest value.
                    if (written > 0)
                        Interlocked.Add(ref _fedSampleCount, written);
                }

                _remainingPreSkip -= skip;
                if (_remainingPreSkip < 0)
                    _remainingPreSkip = 0;
            }
            await WhenPlaybackDrained(cancellationToken).ConfigureAwait(false);
            await End(true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "DecodeAndFeed failed");
            NotifyEnded(e.Message);
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
            var playedSampleCount = audioTrack.PlaybackHeadPosition.Clamp(0, fedSampleCount); // Can't go beyond end
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

        await TaskExt.NeverEnding(pauseEndTokenSource.Token).SilentAwait(false);
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
        var sampleRate = Constants.Audio.PlaybackSampleRate;
        while (CanContinuePlaying(out _)) {
            // Cross-thread read of the count DecodeAndFeed accumulates via Interlocked.Add.
            var remaining = Volatile.Read(ref _fedSampleCount) - GetPlayedSampleCount();
            if (remaining <= 0)
                return;

            var delayMs = (int)Math.Clamp(remaining * 1000L / sampleRate, MinDrainPollMs, PositionReportPeriodMs);
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private static TimeSpan GetEncodedBufferDuration(TimeSpan targetBufferSize)
    {
        var encoded = targetBufferSize - Constants.Audio.DecodedBufferSize - Constants.Audio.AudioEnginePlaybackLatency;
        return TimeSpanExt.Max(encoded, Constants.Audio.MinEncodedBufferSize);
    }
}
