using System.Buffers;
using ActualChat.Media;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Components;
using Android.Media;
using AudioSource = ActualChat.Audio.AudioSource;
using Encoding = Android.Media.Encoding;
using Stream = Android.Media.Stream;

namespace ActualChat.App.Maui.Playback;

internal sealed class AndroidAudioPlaybackEngine(
    string id,
    TrackInfo info,
    IMediaSource source,
    IAudioPlayerBackend playerBackend,
    IServiceProvider services)
    : IAudioPlaybackEngine
{
    private readonly Channel<IMemoryOwner<byte>> _packetChannel = Channel.CreateUnbounded<IMemoryOwner<byte>>(new UnboundedChannelOptions {
        SingleReader = true,
        SingleWriter = true,
        AllowSynchronousContinuations = false,
    });

    private readonly BlockRingBuffer<float> _decodedSamples = new(Constants.Audio.PlaybackSampleRate * 20); // 20-seconds buffer
    private readonly CancellationTokenSource _decodeCts = new();

    private CancellationTokenSource? _pauseCts;

    // Opus decoder pre-skip handling
    private int _remainingPreSkip;

    private AudioTrack? _audioTrack;
    private Task? _decodeTask;
    private Task? _playTask;
    private Task? _delayedPlayTask; // Reference to the delayed Start task to prevent GC

    // Playback reporting state
    private long _playedSamples;
    private volatile bool _isPaused = true;
    private DateTime _lastReportAt = DateTime.MinValue;
    private int _endedReported;

    [field: AllowNull, MaybeNull]
    private IAudioCodec AudioCodec => field ??= services.GetRequiredService<IAudioCodec>();

    [field: AllowNull, MaybeNull]
    private MomentClockSet Clocks => field ??= services.GetRequiredService<MomentClockSet>();

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= services.LogFor<AndroidAudioPlaybackEngine>();

    public async Task Play(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _decodeTask) is not null)
            return;

        var audioSource = (AudioSource)source;
        _remainingPreSkip = audioSource.Format.PreSkip;

        // Configure AudioTrack for float32 PCM mono at playback sample rate
        var sampleRate = Constants.Audio.PlaybackSampleRate;
        var channelOut = ChannelOut.Mono;
        var encoding = Encoding.PcmFloat;
        var minBufferBytes = AudioTrack.GetMinBufferSize(sampleRate, channelOut, encoding);
        if (minBufferBytes <= 0)
            throw new InvalidOperationException($"AudioTrack min buffer size invalid: {minBufferBytes}");

        var bufferBytes = Math.Max(minBufferBytes, Constants.Audio.PcmFrameLength * sizeof(float) * 8); // some headroom
        try {
            _audioTrack = new AudioTrack(
                /* streamType */ Stream.Music,
                /* sampleRateInHz */ sampleRate,
                /* channelConfig */ channelOut,
                /* audioFormat */ encoding,
                /* bufferSizeInBytes */ bufferBytes,
                /* mode */ AudioTrackMode.Stream);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize AudioTrack");
            throw;
        }

        // Start background decode loop
        var ct = _decodeCts.Token;
        _decodeTask = BackgroundTask.Run(() => Decode(ct), ct);
        _playTask = BackgroundTask.Run(() => PlayLoop(ct), ct);
        _isPaused = true;
        // Initial report that we're ready to play
        _ = ReportPlaying();

        // Wait until we have enough decoded samples buffered before starting playback
        _pauseCts = new CancellationTokenSource();
        _delayedPlayTask = StartWhenBuffered(_pauseCts.Token);
    }

    public Task Pause(CancellationToken cancellationToken)
    {
        if (_audioTrack == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");

        try { _audioTrack.Pause(); } catch { /* ignore */ }
        _isPaused = true;
        _pauseCts.CancelAndDisposeSilently();
        _pauseCts = null;
        _delayedPlayTask.DisposeSilently();
        _delayedPlayTask = null;
        _ = ReportPlaying();
        return Task.CompletedTask;
    }

    public Task Resume(CancellationToken cancellationToken)
    {
        if (_audioTrack == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");

        _pauseCts = new CancellationTokenSource();
        _delayedPlayTask = StartWhenBuffered(_pauseCts.Token);
        _isPaused = false;
        _ = ReportPlaying();
        return Task.CompletedTask;
    }

    public Task End(bool abort, CancellationToken cancellationToken)
    {
        if (!abort) {
            _packetChannel.Writer.TryComplete();
            return Task.CompletedTask;
        }

        // Abort immediately
        try {
            _audioTrack?.Pause();
            _audioTrack?.Stop();
        } catch { /* ignore */ }
        _packetChannel.Writer.TryComplete();
        _decodedSamples.Clear();
        _decodeCts.CancelAndDisposeSilently();
        _pauseCts?.CancelAndDisposeSilently();
        _isPaused = true;
        TryReportEnded(null);
        return Task.CompletedTask;
    }

    public ValueTask PushFrame(MediaFrame frame, CancellationToken cancellationToken)
    {
        var data = frame.Data;
        if (data.Length == 0)
            return ValueTask.CompletedTask;

        _packetChannel.Writer.TryWrite(new ByteArrayMemoryOwner(data));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        try {
            _decodeTask.DisposeSilently();
            _playTask.DisposeSilently();
            _delayedPlayTask.DisposeSilently();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) {
            Log.LogError(e, "Failed to dispose AndroidAudioPlaybackEngine tasks");
        }

        try {
            if (_audioTrack != null) {
                try { _audioTrack.Pause(); } catch { /* ignore */ }
                try { _audioTrack.Stop(); } catch { /* ignore */ }
                try { _audioTrack.Release(); } catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
        finally {
            _audioTrack = null;
            _decodeTask = null;
            _playTask = null;
            _delayedPlayTask = null;
        }

        return ValueTask.CompletedTask;
    }

    // Private methods

    private async Task StartWhenBuffered(CancellationToken cancellationToken)
    {
        try {
            // Wait until we have enough decoded samples buffered before starting playback
            var minSamples = (int)(Constants.Audio.StartPlaybackWhenBufferedDuration.TotalSeconds * Constants.Audio.PlaybackSampleRate);
            if (minSamples > 0)
                while (_decodedSamples.Count < minSamples && !cancellationToken.IsCancellationRequested)
                    try {
                        if (_decodedSamples.WhenPushed.IsCompleted) {
                            var remaining = minSamples - _decodedSamples.Count;
                            await Clocks.CoarseSystemClock.Delay(remaining * 1000 / Constants.Audio.PlaybackSampleRate, cancellationToken);
                        }
                        await _decodedSamples.WhenPushed.WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) {
                        return; // Respect cancellation and skip Start
                    }

            var track = _audioTrack;
            if (track is null)
                return;

            try { track.Play(); } catch (Exception e) { Log.LogError(e, "AudioTrack.Play failed"); }
            _isPaused = false;
            _ = ReportPlaying();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to start Android audio playback");
            throw;
        }
    }

    private async Task Decode(CancellationToken cancellationToken)
    {
        try {
            var input = _packetChannel.Reader.ReadAllAsync(cancellationToken);
            await foreach (var pcmOwner in AudioCodec.Decode(input, cancellationToken).ConfigureAwait(false)) {
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
                while (!_decodedSamples.TryPush(pcm.Span.Slice(skip, playSamples))) {
                    await ReportPlaying();
                    await _decodedSamples.WhenPulled.WaitAsync(cancellationToken);
                }

                _remainingPreSkip -= skip;
                if (_remainingPreSkip < 0)
                    _remainingPreSkip = 0;
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception e) {
            Log.LogError(e, "Decode loop failed");
            TryReportEnded(e.Message);
        }
    }

    private async Task PlayLoop(CancellationToken cancellationToken)
    {
        var silence = new float[Constants.Audio.PcmFrameLength];
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var track = _audioTrack;
                if (track == null) {
                    await Clocks.CoarseSystemClock.Delay(5, cancellationToken);
                    continue;
                }

                var required = Constants.Audio.PcmFrameLength;
                if (!_decodedSamples.TryPull(required, out var pcmOwner)) {
                    // No data yet
                    if (_packetChannel.Reader.Completion.IsCompleted) {
                        // Stream ended and buffer is empty -> finish
                        End(true, CancellationToken.None);
                        break;
                    }

                    // Report starving state and write silence if playing
                    _ = ReportPlaying();
                    if (!_isPaused && track.PlayState == PlayState.Playing) {
                        var written = track.Write(silence, 0, silence.Length, WriteMode.Blocking);
                        if (written > 0)
                            _playedSamples += written;
                    }

                    // Avoid busy spin
                    await Clocks.CoarseSystemClock.Delay(5, cancellationToken);
                    continue;
                }

                using (pcmOwner) {
                    var span = pcmOwner.Memory.Span;
                    if (_isPaused || track.PlayState != PlayState.Playing) {
                        // If paused, don't write to track; give back buffer and wait
                        // But we already pulled; just don't advance playedSamples
                        // Write a tiny delay to avoid tight loop
                        await Clocks.CoarseSystemClock.Delay(5, cancellationToken);
                        continue;
                    }

                    // AudioTrack.Write has overload for float[] only; copy span
                    var tmp = ArrayPool<float>.Shared.Rent(span.Length);
                    try {
                        span.CopyTo(tmp.AsSpan(0, span.Length));
                        var written = track.Write(tmp, 0, span.Length, WriteMode.Blocking);
                        if (written > 0)
                            _playedSamples += written;
                    }
                    finally {
                        ArrayPool<float>.Shared.Return(tmp);
                    }

                    var now = DateTime.UtcNow;
                    if ((now - _lastReportAt).TotalMilliseconds >= 200) {
                        _lastReportAt = now;
                        _ = ReportPlaying();
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception e) {
            Log.LogError(e, "Playback loop failed");
            TryReportEnded(e.Message);
        }
    }

    private Task ReportPlaying()
    {
        var played = (double)_playedSamples / Constants.Audio.PlaybackSampleRate;
        var buffered = TimeSpan.FromSeconds((double)_decodedSamples.Count / Constants.Audio.PlaybackSampleRate);
        var isBufferLow = buffered < Constants.Audio.LowPlaybackBufferDuration;
        try {
            return playerBackend.OnPlaying(played, _isPaused, isBufferLow);
        }
        catch {
            return Task.CompletedTask;
        }
    }

    private void TryReportEnded(string? message)
    {
        if (Interlocked.Exchange(ref _endedReported, 1) != 0)
            return;
        _ = SafeOnEnded(message);
    }

    private async Task SafeOnEnded(string? message)
    {
        try {
            await playerBackend.OnEnded(message);
        }
        catch {
             /* ignore */
        }
    }
}
