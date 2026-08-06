using ActualChat.Audio;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Components;
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
    private volatile AudioTrack? _audioTrack;
    private volatile Task? _decodeAndFeedTask;
    private volatile Task? _positionWatchTask;
    private GCHandle _audioTrackHandle;

    private int _remainingPreSkip;
    private volatile int _fedSampleCount;
    private volatile int _lastPlayedSampleCount;
    private int _isEnded;
    private long _nextLagReportAtTicks;

    private IAudioCodec AudioCodec => field ??= services.GetRequiredService<IAudioCodec>();
    private MomentClockSet Clocks => field ??= services.GetRequiredService<MomentClockSet>();
    private ILogger Log => field ??= services.LogFor<AndroidAudioPlaybackEngine>();

    public async Task Play(CancellationToken cancellationToken)
    {
        Log.LogDebug("Play called: id={Id}", info.TrackId);
        if (_decodeAndFeedTask is not null)
            return;

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
            var attributes = new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.VoiceCommunication)!
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
            _lastPlayedSampleCount = 0;
            _pauseEndTokenSource = null;

            _audioTrack = audioTrack;
            _audioTrackHandle = GCHandle.Alloc(audioTrack, GCHandleType.Normal);
            _decodeAndFeedTask = BackgroundTask.Run(DecodeAndFeed, CancellationToken.None);
            _positionWatchTask = BackgroundTask.Run(WatchPlaybackPosition, CancellationToken.None);
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
        if (_decodeAndFeedTask is not null) {
            await _decodeAndFeedTask.SilentAwait();
            _decodeAndFeedTask = null;
        }
        if (_positionWatchTask is not null) {
            await _positionWatchTask.SilentAwait();
            _positionWatchTask = null;
        }

        lock (Lock) { // We must re-lock after await
            if (_audioTrack.IsValid()) {
                try {
                    if (_audioTrack.PlayState is PlayState.Playing or PlayState.Paused)
                        _audioTrack.Stop();
                }
                catch { /* Ignore */ }
                try { _audioTrack.Release(); } catch { /* Ignore */ }
                _audioTrack.DisposeSilently();
            }
            _audioTrack = null;

            if (_audioTrackHandle.IsAllocated)
                _audioTrackHandle.Free();
        }
    }

    public Task Pause(CancellationToken cancellationToken)
    {
        if (_audioTrack is null)
            throw StandardError.AudioPlayer.PlayingStateExpected(GetType());

        _audioTrack.Pause();
        // Enter paused state: create a gate CTS that will be canceled on Resume()
        _pauseEndTokenSource?.CancelAndDisposeSilently();
        _pauseEndTokenSource = new CancellationTokenSource();
        NotifyPlaying(GetPlayedSampleCount());
        return Task.CompletedTask;
    }

    public Task Resume(CancellationToken cancellationToken)
    {
        if (_audioTrack is null)
            throw StandardError.AudioPlayer.PlayingStateExpected(GetType());

        _pauseEndTokenSource?.CancelAndDisposeSilently();
        _pauseEndTokenSource = null;
        _audioTrack.Play();
        NotifyPlaying(GetPlayedSampleCount());
        return Task.CompletedTask;
    }

    public Task End(bool mustAbort, CancellationToken cancellationToken)
    {
        var enqueuedSampleCount =_frames.Count;
        Log.LogDebug(
            "End called: id={Id} abort={Abort} enqueued={Frames} fed={FeedSampleCount}",
            info.TrackId, mustAbort, enqueuedSampleCount, _fedSampleCount);

        var audioTrack = _audioTrack.IfValid();
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
        var audioTrack = _audioTrack!;
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
                    var written = await audioTrack.WriteAsync(audioData, skip, playSamples, WriteMode.Blocking).ConfigureAwait(false);
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
        var buffered = TimeSpan.FromSeconds((double)(_fedSampleCount - playedSampleCount) / Constants.Audio.PlaybackSampleRate);
        var isBufferLow = buffered < Constants.Audio.LowPlaybackBufferDuration;
        var isPaused = !_audioTrack.IsValid() || _audioTrack.PlayState is PlayState.Paused or PlayState.Stopped;
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
        audioTrack = _audioTrack;
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
        if (!CanContinuePlaying(out var audioTrack))
            return Math.Max(_fedSampleCount, _lastPlayedSampleCount); // Pretend we played everything

        try {
            var playedSampleCount = audioTrack.PlaybackHeadPosition.Clamp(0, _fedSampleCount); // Can't go beyond end
            playedSampleCount = Math.Max(playedSampleCount, _lastPlayedSampleCount); // Can't decrease
            Interlocked.Exchange(ref _lastPlayedSampleCount, playedSampleCount);
            return playedSampleCount;
        }
        catch {
            return Math.Max(_fedSampleCount, _lastPlayedSampleCount); // Pretend we played everything
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
            var remaining = _fedSampleCount - GetPlayedSampleCount();
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
