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

    // Last known number of played samples (playback head), used as a fallback when AudioTrack is not queryable
    private int _lastPlayedSamples;

    // Playback reporting state
    private int _feedSamples;
    private int _endedReported;

    [field: AllowNull, MaybeNull]
    private IAudioCodec AudioCodec => field ??= services.GetRequiredService<IAudioCodec>();

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
        var bufferBytes = Math.Max(minBufferBytes, Constants.Audio.PcmFrameLength * 4);
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
                .SetSessionId(AudioManager.AudioSessionIdGenerate)
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

        // Reset last known head position before starting playback
        _lastPlayedSamples = 0;

        _audioTrack.Play();
        // Initial report that we're ready to play
        _ = ReportPlaying(0);

        // Ensure not paused initially
        _pauseCts = null;
    }

    public Task Pause(CancellationToken cancellationToken)
    {
        if (_audioTrack == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");

        _audioTrack.Pause();
        // Enter paused state: create a gate CTS that will be canceled on Resume()
        _pauseCts?.CancelAndDisposeSilently();
        _pauseCts = new CancellationTokenSource();
        _ = ReportPlaying(GetSafePlayedSamples(_audioTrack));
        return Task.CompletedTask;
    }

    public Task Resume(CancellationToken cancellationToken)
    {
        if (_audioTrack == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");

        // Exit paused state: cancel and dispose pause gate, then resume playback
        _pauseCts?.CancelAndDisposeSilently();
        _pauseCts = null;
        _audioTrack.Play();
        _ = ReportPlaying(GetSafePlayedSamples(_audioTrack));
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
            if (_audioTrack is { PlayState: PlayState.Playing or PlayState.Paused } audioTrack)
                audioTrack.Stop();
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
            _decodeCts.CancelAndDisposeSilently();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) {
            Log.LogError(e, "Failed to dispose AndroidAudioPlaybackEngine tasks");
        }

        try {
            if (_audioTrack != null) {
                try {
                    if (_audioTrack is { PlayState: PlayState.Playing or PlayState.Paused } audioTrack)
                        audioTrack.Stop();
                } catch { /* ignore */ }
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
                        Log.LogDebug("AudioTrack is null during DecodeAndFeed, terminating");
                        _packetChannel.Writer.TryComplete();
                        await End(true, cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    // If playback is paused, wait until it is resumed before feeding more data
                    await WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

                    // Verify track is still valid before writing
                    // Check if track was stopped or released while we were waiting
                    if (!IsTrackValidForWrite(track)) {
                        Log.LogDebug("AudioTrack became invalid during DecodeAndFeed, terminating");
                        // Clean up the channel to prevent further processing
                        _packetChannel.Writer.TryComplete();
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
            var currentTrack = _audioTrack;
            var listener = _listener;
            if (currentTrack != null && listener != null && IsTrackValidForWrite(currentTrack)) {
                var played = GetSafePlayedSamples(currentTrack);
                if (_feedSamples > played) {
                    currentTrack.SetNotificationMarkerPosition(_feedSamples);
                    await listener.WhenCompleted.WaitAsync(cancellationToken).ConfigureAwait(false);
                } else
                    TryReportEnded(null);
            } else
                TryReportEnded(null);
            await End(true, CancellationToken.None).ConfigureAwait(false);
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
        var buffered = TimeSpan.FromSeconds((double)(_feedSamples - playedSamples) / Constants.Audio.PlaybackSampleRate);
        var isBufferLow = buffered < Constants.Audio.LowPlaybackBufferDuration;

        bool isPaused;
        var track = _audioTrack;
        if (track == null || track.Handle == IntPtr.Zero)
            isPaused = true; // Assume ended/disposed
        else
            try {
                isPaused = track.PlayState is PlayState.Paused or PlayState.Stopped;
            }
            catch (Exception e) {
                Log.LogWarning(e, "Error checking PlayState in ReportPlaying, assuming paused");
                isPaused = true;
            }

        try {
            return playerBackend.OnPlaying(played, isPaused, isBufferLow);
        }
        catch {
            return Task.CompletedTask;
        }
    }

    // Safely read playback head position without throwing IllegalStateException
    private int GetSafePlayedSamples(AudioTrack? track)
    {
        try {
            if (track == null || track.Handle == IntPtr.Zero)
                return GetFeedSamples();

            // Avoid querying head position when the track is stopped or not initialized (released)
            var playState = track.PlayState;
            if (playState == PlayState.Stopped)
                return GetFeedSamples();

            var state = track.State;
            // Only query when initialized; after Release() it's typically Uninitialized
            if (state != AudioTrackState.Initialized)
                return GetFeedSamples();

            var head = track.PlaybackHeadPosition; // may throw in illegal state
            // Clamp to [0, _feedSamples] and avoid regressions
            var clamped = Math.Max(0, Math.Min(head, _feedSamples));
            if (clamped < _lastPlayedSamples)
                clamped = _lastPlayedSamples;
            _lastPlayedSamples = clamped;
            return clamped;
        }
        catch (Java.Lang.IllegalStateException) {
            // GetFeedSamples to last known safe value within buffered range
            return GetFeedSamples();
        }
        catch {
            return GetFeedSamples();
        }

        int GetFeedSamples() => Math.Min(_feedSamples, _lastPlayedSamples);
    }

    private async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        var gate = Volatile.Read(ref _pauseCts);
        if (gate is null)
            return;

        try {
            await Task.Delay(-1, gate.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            // resumed
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void TryReportEnded(string? message)
    {
        if (Interlocked.Exchange(ref _endedReported, 1) != 0)
            return;

        _ = playerBackend.OnEnded(message);
    }

    private static bool IsTrackValidForWrite(AudioTrack? track)
    {
        if (track is null)
            return false;

        try {
            // Check if track state is initialized (not released)
            var state = track.State;
            if (state != AudioTrackState.Initialized)
                return false;

            // Check if track is not stopped
            var playState = track.PlayState;
            return playState != PlayState.Stopped;
        }
        catch {
            // If we can't read the state, assume it's invalid
            return false;
        }
    }

    private sealed class PlayPositionListener(AndroidAudioPlaybackEngine parent)
        : Java.Lang.Object, AudioTrack.IOnPlaybackPositionUpdateListener
    {
        private readonly TaskCompletionSource<bool> _whenCompletedSource = new (TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> WhenCompleted => _whenCompletedSource.Task;

        public void OnMarkerReached(AudioTrack? track)
        {
            parent.Log.LogDebug("AudioTrack marker reached");
            parent._lastPlayedSamples = parent._feedSamples; // Align to end position
            _whenCompletedSource.TrySetResult(true);

            int head;
            if (track is null || track.Handle == IntPtr.Zero)
                head = parent._lastPlayedSamples;
            else
                head = parent.GetSafePlayedSamples(track);
            parent.ReportPlaying(head);
        }

        public void OnPeriodicNotification(AudioTrack? track)
        {
            // parent.Log.LogDebug("AudioTrack periodic notification");
            if (track is null) {
                _whenCompletedSource.TrySetResult(false);
                var fallback = parent.GetSafePlayedSamples(null);
                parent.ReportPlaying(fallback);
                return;
            }

            var head = parent.GetSafePlayedSamples(track);
            parent.ReportPlaying(head);
        }
    }
}
