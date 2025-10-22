using System.Buffers;
using ActualChat.Media;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Components;
using Android.Media;
using AudioSource = ActualChat.Audio.AudioSource;
using Encoding = Android.Media.Encoding;

namespace ActualChat.App.Maui.Audio;

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

    private readonly CancellationTokenSource _decodeCts = new();

    private CancellationTokenSource? _pauseCts;

    // Opus decoder pre-skip handling
    private int _remainingPreSkip;

    private AudioTrack? _audioTrack;
    private Task? _decodeAndFeedTask;
    private PlayPositionListener? _listener;

    // Playback reporting state
    private int _feedSamples;
    private int _endedReported;

    [field: AllowNull, MaybeNull]
    private IAudioCodec AudioCodec => field ??= services.GetRequiredService<IAudioCodec>();

    [field: AllowNull, MaybeNull]
    private MomentClockSet Clocks => field ??= services.GetRequiredService<MomentClockSet>();

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= services.LogFor<AndroidAudioPlaybackEngine>();

    public async Task Play(CancellationToken cancellationToken)
    {
        Log.LogDebug("Play called: id={Id}", info.TrackId);
        if (Volatile.Read(ref _decodeAndFeedTask) is not null)
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

        // Minimum buffer size to keep Feed blocked on track.WriteAsync
        var bufferBytes = Math.Max(minBufferBytes, Constants.Audio.PcmFrameLength);
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

            _audioTrack = new AudioTrack.Builder()
                .SetAudioAttributes(attributes!)
                .SetAudioFormat(audioFormat!)
                .SetBufferSizeInBytes(bufferBytes)
                .SetTransferMode(AudioTrackMode.Stream)
                .SetSessionId(AudioManager.AudioSessionIdGenerate)!
                .Build();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize AudioTrack");
            throw;
        }

        // Start background decode loop
        var ct = _decodeCts.Token;
        _decodeAndFeedTask = BackgroundTask.Run(() => DecodeAndFeed(ct), ct);

        _listener = new PlayPositionListener(this);
        // Reasonable period to receive callbacks; not too frequent
        _audioTrack.SetPlaybackPositionUpdateListener(_listener);
        _audioTrack.SetPositionNotificationPeriod(Constants.Audio.PcmFrameLength * 10); // 200 ms
        _audioTrack.Play();
        // Initial report that we're ready to play
        _ = ReportPlaying(0);

        // Wait until we have enough decoded samples buffered before starting playback
        _pauseCts = new CancellationTokenSource();
    }

    public Task Pause(CancellationToken cancellationToken)
    {
        if (_audioTrack == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");

        _audioTrack.Pause();
        _pauseCts.CancelAndDisposeSilently();
        _pauseCts = null;
        _ = ReportPlaying(_audioTrack.PlaybackHeadPosition);
        return Task.CompletedTask;
    }

    public Task Resume(CancellationToken cancellationToken)
    {
        if (_audioTrack == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");

        _pauseCts = new CancellationTokenSource();
        _audioTrack.Play();
        _ = ReportPlaying(_audioTrack.PlaybackHeadPosition);
        return Task.CompletedTask;
    }

    public Task End(bool abort, CancellationToken cancellationToken)
    {
        var frames = _packetChannel.Reader.CanCount
            ? _packetChannel.Reader.Count
            : -1;
        var feedSamples = _feedSamples;
        Log.LogDebug("End called: id={Id} abort={Abort} scheduled={Frames} feed={FeedSamples}", info.TrackId, abort, frames, feedSamples);
        if (!abort) {
            _packetChannel.Writer.TryComplete();
            var track = _audioTrack;
            if (track is null)
                return Task.CompletedTask;

            if (track.PlayState == PlayState.Stopped)
                track.Play(); // Start playback if stopped (not paused)
            return Task.CompletedTask;
        }

        // Abort immediately
        try {
            _audioTrack?.Stop();
        } catch { /* ignore */ }
        _packetChannel.Writer.TryComplete();
        _decodeCts.CancelAndDisposeSilently();
        _pauseCts?.CancelAndDisposeSilently();
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
            _decodeAndFeedTask.DisposeSilently();
            _decodeCts.CancelAndDisposeSilently();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) {
            Log.LogError(e, "Failed to dispose AndroidAudioPlaybackEngine tasks");
        }

        try {
            if (_audioTrack != null) {
                try { _audioTrack.Stop(); } catch { /* ignore */ }
                try { _audioTrack.Release(); } catch { /* ignore */ }
                _audioTrack.DisposeSilently();
            }
        }
        finally {
            _audioTrack = null;
            _decodeAndFeedTask = null;
        }

        return ValueTask.CompletedTask;
    }

    // Private methods

    private async Task DecodeAndFeed(CancellationToken cancellationToken)
    {
        var audioData = new float[Constants.Audio.PcmFrameLength];
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
                if (playSamples > 0) {
                    var track = _audioTrack;
                    if (track is null) {
                        await End(true, cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    pcm.Span.CopyTo(audioData.AsSpan(0, pcm.Length));
                    var written = await track.WriteAsync(audioData, skip, playSamples, WriteMode.Blocking).ConfigureAwait(false);
                    if (written > 0)
                        _feedSamples += written;
                }

                _remainingPreSkip -= skip;
                if (_remainingPreSkip < 0)
                    _remainingPreSkip = 0;
            }
            if (_audioTrack is { } currentTrack && _listener is { } listener) {
                currentTrack.SetNotificationMarkerPosition(_feedSamples);
                await listener.WhenCompleted;
                await End(true, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* ignore */ }
        catch (Exception e) {
            Log.LogError(e, "DecodeAndFeed loop failed");
            TryReportEnded(e.Message);
        }
    }

    private Task ReportPlaying(int playedSamples)
    {
        var played = (double)playedSamples / Constants.Audio.PlaybackSampleRate;
        var buffered = TimeSpan.FromSeconds((double)_feedSamples / Constants.Audio.PlaybackSampleRate);
        var isBufferLow = buffered < Constants.Audio.LowPlaybackBufferDuration;
        var isPaused = _audioTrack?.PlayState is PlayState.Paused or PlayState.Stopped;
        try {
            return playerBackend.OnPlaying(played, isPaused, isBufferLow);
        }
        catch {
            return Task.CompletedTask;
        }
    }

    private void TryReportEnded(string? message)
    {
        if (Interlocked.Exchange(ref _endedReported, 1) != 0)
            return;

        _ = playerBackend.OnEnded(message);
    }

    private sealed class PlayPositionListener(AndroidAudioPlaybackEngine parent)
        : Java.Lang.Object, AudioTrack.IOnPlaybackPositionUpdateListener
    {
        private readonly TaskCompletionSource<bool> _whenCompletedSource = new (TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> WhenCompleted => _whenCompletedSource.Task;

        public void OnMarkerReached(AudioTrack? track)
        {
            parent.Log.LogDebug("AudioTrack marker reached");
            _whenCompletedSource.TrySetResult(true);
            if (track is null)
                return;

            var head = track.PlaybackHeadPosition;
            parent.ReportPlaying(head);
        }

        public void OnPeriodicNotification(AudioTrack? track)
        {
            // parent.Log.LogDebug("AudioTrack periodic notification");
            if (track is null) {
                _whenCompletedSource.TrySetResult(false);
                return;
            }

            var head = track.PlaybackHeadPosition;
            parent.ReportPlaying(head);
        }
    }
}
