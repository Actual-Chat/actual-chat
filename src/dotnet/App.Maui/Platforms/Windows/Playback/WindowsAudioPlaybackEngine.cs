#if WINDOWS
using System.Buffers;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.MediaProperties;
using ActualChat.Audio;
using ActualChat.MediaPlayback;
using ActualChat.Media;
using ActualChat.UI.Blazor.App.Components;
using AudioFrame = Windows.Media.AudioFrame;

namespace  ActualChat.App.Maui.Playback;

internal sealed class WindowsAudioPlaybackEngine(
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

    // According to Opus spec, the decoder output must skip the first PreSkip samples at 48 kHz
    // We track how many samples remain to be skipped and drop them in the decode loop.
    private int _remainingPreSkip;
    private AudioGraph? _graph;
    private AudioDeviceOutputNode? _deviceOutput;
    private AudioFrameInputNode? _frameInput;
    private Task? _decodeTask;
    private Task? _delayedPlayTask; // Reference to the delayed Start task to prevent GC

    // Playback reporting state
    private long _playedSamples;
    private volatile bool _isPaused = true;
    private DateTime _lastReportAt = DateTime.MinValue;
    private int _endedReported;

    [field: AllowNull, MaybeNull]
    private IAudioCodec AudioCodec => field ??= services.GetRequiredService<IAudioCodec>();

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= services.LogFor<WindowsAudioPlaybackEngine>();

    public async Task Play(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _decodeTask) is not null)
            return;

        var audioSource = (AudioSource)source;
        // Initialize pre-skip samples to drop from the beginning of decoded PCM
        _remainingPreSkip = audioSource.Format.PreSkip;

        // Configure Float32 mono PCM at our sample rate
        var encoding = AudioEncodingProperties.CreatePcm(
            Constants.Audio.PlaybackSampleRate,
            Constants.Audio.Channels,
            32);
        encoding.Subtype = MediaEncodingSubtypes.Float;

        var settings = new AudioGraphSettings(Windows.Media.Render.AudioRenderCategory.Media) {
            QuantumSizeSelectionMode = QuantumSizeSelectionMode.ClosestToDesired,
            DesiredSamplesPerQuantum = Constants.Audio.RecordingSampleRate / 1000 * Constants.Audio.OpusFrameDurationMs,
        };

        var graphCreate = await AudioGraph.CreateAsync(settings).AsTask(cancellationToken);
        if (graphCreate.Status != AudioGraphCreationStatus.Success || graphCreate.Graph is null)
            throw new InvalidOperationException($"AudioGraph creation failed: {graphCreate.Status}");

        _graph = graphCreate.Graph;

        var deviceOutputResult = await _graph.CreateDeviceOutputNodeAsync().AsTask(cancellationToken);
        if (deviceOutputResult.Status != AudioDeviceNodeCreationStatus.Success || deviceOutputResult.DeviceOutputNode is null)
            throw new InvalidOperationException($"AudioGraph device output creation failed: {deviceOutputResult.Status}");
        _deviceOutput = deviceOutputResult.DeviceOutputNode;
        _frameInput = _graph.CreateFrameInputNode(encoding);
        _frameInput.AddOutgoingConnection(_deviceOutput);
        _frameInput.QuantumStarted += OnQuantumStarted;

        // Start background decode loop
        var ct = _decodeCts.Token;
        _decodeTask = BackgroundTask.Run(() => DecodeAndFeed(ct), ct);
        _isPaused = true;
        // Initial report that we're ready to play
        _ = ReportPlaying();

        // Wait until we have enough decoded samples buffered before starting playback
        _pauseCts = new CancellationTokenSource();
        _delayedPlayTask = StartWhenBuffered(_pauseCts.Token);
    }

    public Task Pause(CancellationToken cancellationToken)
    {
        if (_frameInput == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");

        _frameInput.Stop();
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
        if (_frameInput == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");

        _pauseCts = new CancellationTokenSource();
        _delayedPlayTask = StartWhenBuffered(_pauseCts.Token);
        _isPaused = false;
        _ = ReportPlaying();
        return Task.CompletedTask;
    }

    public Task End(bool abort, CancellationToken cancellationToken)
    {
        _packetChannel.Writer.TryComplete();
        _decodedSamples.Clear();
        _decodeCts.CancelAndDisposeSilently();
        _pauseCts?.CancelAndDisposeSilently();
        try {
            _decodeTask.DisposeSilently();
            _delayedPlayTask.DisposeSilently();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) {
            Log.LogError(e, "Failed to wait for decode loop to complete");
             /* ignore */
        }

        if (_graph != null) {
            try {
                _frameInput?.Stop();
                _graph.Stop();
            } catch { /* ignore */ }
            _frameInput.DisposeSilently();
            _deviceOutput.DisposeSilently();
            _graph.DisposeSilently();
        }
        _graph = null;
        _deviceOutput = null;
        _frameInput = null;
        _decodeTask = null;
        _delayedPlayTask = null;
        _isPaused = true;
        // Report end (no error message). If an error already reported, this will no-op.
        TryReportEnded(null);
        return Task.CompletedTask;
    }

    public Task Frame(MediaFrame frame, CancellationToken cancellationToken)
    {
        // Enqueue opus packet
        var data = frame.Data;
        if (data.Length == 0)
            return Task.CompletedTask;

        _packetChannel.Writer.TryWrite(new ByteArrayMemoryOwner(data));
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
        => await End(true, CancellationToken.None).ConfigureAwait(false);


    // Private methods

    private async Task StartWhenBuffered(CancellationToken cancellationToken)
    {
        try {
            // Wait until we have enough decoded samples buffered before starting playback
            var minSamples = (int)(Constants.Audio.StartPlaybackWhenBufferedDuration.TotalSeconds * Constants.Audio.PlaybackSampleRate);
            if (minSamples > 0)
                while (_decodedSamples.Count < minSamples && !cancellationToken.IsCancellationRequested)
                    try {
                        await _decodedSamples.WhenPushed.WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) {
                        // Respect cancellation and skip Start
                        return;
                    }

            var (frameInput, graph) = (_frameInput, _graph);
            if (frameInput is null || graph is null)
                return;

            frameInput.Start();
            graph.Start();
            _isPaused = false;
            // Initial report that we're ready to play
            _ = ReportPlaying();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to start playback graph");
            throw;
        }
    }

    private async Task DecodeAndFeed(CancellationToken cancellationToken)
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
                        // Entire buffer skipped
                        continue;
                    }

                var skip = Math.Min(_remainingPreSkip, samples);
                var playSamples = samples - skip;
                while (!_decodedSamples.TryPush(pcm.Span.Slice(skip, playSamples))) {
                    // Report buffer full
                    await ReportPlaying();
                    await _decodedSamples.WhenPulled.WaitAsync(cancellationToken);
                }

                // Update played samples and periodically report playing state
                _remainingPreSkip -= skip;
                if (_remainingPreSkip < 0)
                    _remainingPreSkip = 0;
            }

            // Source completed – report ended
            TryReportEnded(null);
        }
        catch (OperationCanceledException) {
            /* ignore */
        }
        catch (Exception e) {
            Log.LogError(e, "Decode/Feed loop failed");
            TryReportEnded(e.Message);
        }
    }

    private void OnQuantumStarted(AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args)
    {
        var playSamples = args.RequiredSamples;
        var bytes = playSamples * sizeof(float);
        using var audioFrame = new AudioFrame((uint)bytes);
        using var buffer = audioFrame.LockBuffer(AudioBufferAccessMode.Write);
        using var reference = buffer.CreateReference();
        unsafe {
            WindowsRuntimeMarshal.TryGetDataUnsafe(reference, out IntPtr dataPtr, out var capacity);
            if (dataPtr == IntPtr.Zero || capacity < bytes)
                return;

            if (!_decodedSamples.TryPull(playSamples, out var pcmOwner)) {
                // Starving - report playing state
                _ = ReportPlaying();
                // Set frame to silence
                var silence = new Span<float>((void*)dataPtr, playSamples);
                silence.Fill(0);
                _frameInput?.AddFrame(audioFrame);
                return;
            }

            var src = pcmOwner.Memory.Span;
            var dst = new Span<float>((void*)dataPtr, playSamples);
            src.CopyTo(dst);
        }
        _frameInput?.AddFrame(audioFrame);
        _playedSamples += playSamples;
        var now = DateTime.UtcNow;
        if ((now - _lastReportAt).TotalMilliseconds >= 200) {
            _lastReportAt = now;
            _ = ReportPlaying();
        }
    }

    private Task ReportPlaying()
    {
        // Compute offset in seconds from played samples
        var played = (double)_playedSamples / Constants.Audio.PlaybackSampleRate;
        var buffered = TimeSpan.FromSeconds((double)_decodedSamples.Count / Constants.Audio.PlaybackSampleRate);
        var isBufferLow = buffered < Constants.Audio.LowPlaybackBufferDuration;
        try {
            return playerBackend.OnPlaying(played, _isPaused, isBufferLow);
        }
        catch {
            // Don't propagate reporting errors
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
            // ignore
        }
    }

    private readonly struct ByteArrayMemoryOwner(byte[] buffer) : IMemoryOwner<byte>
    {
        public Memory<byte> Memory { get; } = buffer;

        public void Dispose()
        { }
    }
}
#endif
