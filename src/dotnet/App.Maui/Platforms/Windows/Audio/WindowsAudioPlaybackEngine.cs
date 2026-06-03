#if WINDOWS
using System.Buffers;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.MediaProperties;
using ActualChat.Audio;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Components;
using AudioFrame = Windows.Media.AudioFrame;

namespace  ActualChat.App.Maui.Audio;

internal sealed class WindowsAudioPlaybackEngine(
    string id,
    TrackInfo info,
    IMediaSource source,
    IAudioPlayerBackend playerBackend,
    IServiceProvider services
    ) : IAudioPlaybackEngine
{
    private readonly DurationTargetingFrameBuffer<ActualChat.Audio.AudioFrame> _packetBuffer = new(
        static frame => frame.Offset,
        static frame => frame.Duration);

    private readonly BlockRingBuffer<float> _decodeBuffer = new(Constants.Audio.PlaybackSampleRate * 20); // 20-seconds buffer
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

    private const long LagReportIntervalMs = 500;

    // Playback reporting state
    private long _playedSamples;
    private volatile bool _isPaused;
    private DateTime _lastReportAt = DateTime.MinValue;
    private long _nextLagReportAtTicks;
    private int _endedReported;
    private int _decodeCompleted;

    private IAudioCodec AudioCodec => field ??= services.GetRequiredService<IAudioCodec>();

    private MomentClockSet Clocks => field ??= services.GetRequiredService<MomentClockSet>();

    private ILogger Log => field ??= services.LogFor<WindowsAudioPlaybackEngine>();

    public async Task Play(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _decodeTask) is not null)
            return;

        var audioSource = (AudioSource)source;
        // Initialize pre-skip samples to drop from the beginning of decoded PCM
        _remainingPreSkip = audioSource.Format.PreSkip;
        _packetBuffer.SetTargetDuration(GetEncodedBufferDuration(info.TargetBufferSize));

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

        var graphCreate = await AudioGraph.CreateAsync(settings).AsTask(cancellationToken).ConfigureAwait(false);
        if (graphCreate.Status != AudioGraphCreationStatus.Success || graphCreate.Graph is null)
            throw new InvalidOperationException($"AudioGraph creation failed: {graphCreate.Status}");

        _graph = graphCreate.Graph;

        var deviceOutputResult = await _graph.CreateDeviceOutputNodeAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (deviceOutputResult.Status != AudioDeviceNodeCreationStatus.Success || deviceOutputResult.DeviceOutputNode is null)
            throw new InvalidOperationException($"AudioGraph device output creation failed: {deviceOutputResult.Status}");
        _deviceOutput = deviceOutputResult.DeviceOutputNode;
        _frameInput = _graph.CreateFrameInputNode(encoding);
        _frameInput.AddOutgoingConnection(_deviceOutput);
        _frameInput.QuantumStarted += OnQuantumStarted;

        // Start background decode loop
        var ct = _decodeCts.Token;
        _decodeTask = BackgroundTask.Run(() => DecodeAndFeed(ct), ct);

        // Wait until we have enough decoded samples buffered before starting playback
        _pauseCts = new CancellationTokenSource();
        _delayedPlayTask = StartWhenBuffered(_pauseCts.Token);
    }

    public Task Pause(CancellationToken cancellationToken)
    {
        if (_frameInput == null)
            throw StandardError.AudioPlayer.PlayingStateExpected(GetType());

        _frameInput.Stop();
        _isPaused = true;
        _pauseCts.CancelAndDisposeSilently();
        _pauseCts = null;
        _delayedPlayTask = null;
        ReportPlaying();
        return Task.CompletedTask;
    }

    public Task Resume(CancellationToken cancellationToken)
    {
        if (_frameInput == null)
            throw StandardError.AudioPlayer.PlayingStateExpected(GetType());

        _pauseCts = new CancellationTokenSource();
        _delayedPlayTask = StartWhenBuffered(_pauseCts.Token);
        _isPaused = false;
        ReportPlaying();
        return Task.CompletedTask;
    }

    public Task End(bool mustAbort, CancellationToken cancellationToken)
    {
        if (!mustAbort) {
            // Normal end: indicate no more input packets; let decode/playback drain and finish naturally
            _packetBuffer.Complete();
            return Task.CompletedTask;
        }

        // Abort: stop everything immediately
        try {
            _frameInput?.Stop();
            _graph?.Stop();
        } catch { /* Ignore */ }
        _packetBuffer.Complete();
        _decodeBuffer.Clear();
        _decodeCts.CancelAndDisposeSilently();
        _pauseCts?.CancelAndDisposeSilently();
        // Report end (no error message). If an error already reported, this will no-op.
        TryReportEnded(null);
        return Task.CompletedTask;
    }

    public ValueTask PushFrame(ActualChat.Audio.AudioFrame frame, CancellationToken cancellationToken)
    {
        // Enqueue opus packet
        var data = frame.Data;
        if (data.Length == 0)
            return ValueTask.CompletedTask;

        _packetBuffer.Push(frame);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        try {
            _decodeCts.CancelAndDisposeSilently();
            _pauseCts.CancelAndDisposeSilently();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) {
            Log.LogError(e, "Failed to wait for decode loop to complete");
            /* Ignore */
        }

        if (_graph != null) {
            _frameInput.DisposeSilently();
            _deviceOutput.DisposeSilently();
            _graph.DisposeSilently();
        }
        _graph = null;
        _deviceOutput = null;
        _frameInput = null;
        _decodeTask = null;
        _delayedPlayTask = null;
        return ValueTask.CompletedTask;
    }

    // Private methods

    private async Task StartWhenBuffered(CancellationToken cancellationToken)
    {
        try {
            // Wait until we have enough decoded samples buffered before starting playback
            var minSamples = (int)(Constants.Audio.StartPlaybackWhenBufferedDuration.TotalSeconds * Constants.Audio.PlaybackSampleRate);
            if (minSamples > 0)
                while (_decodeBuffer.Count < minSamples && !cancellationToken.IsCancellationRequested)
                    try {
                        var count = _decodeBuffer.Count;
                        if (count > 0) {
                            // Buffer has data but not enough yet; wait estimated time for the rest
                            var remaining = minSamples - count;
                            await Clocks.CoarseSystemClock
                                .Delay(remaining * 1000 / Constants.Audio.PlaybackSampleRate, cancellationToken)
                                .ConfigureAwait(false);
                        }
                        else {
                            // Buffer is empty; wait for data without consuming
                            var whenReady = _decodeBuffer.WhenReadyToRead();
                            if (whenReady != null)
                                await whenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
                            else
                                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                        }
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
            ReportPlaying();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to start playback graph");
            throw;
        }
    }

    private async Task DecodeAndFeed(CancellationToken cancellationToken)
    {
        try {
            var input = _packetBuffer.ReadAllAsync(cancellationToken);
            await foreach (var pcmOwner in AudioCodec.Decode(input, cancellationToken).ConfigureAwait(false)) {
                using var _ = pcmOwner;
                var pcm = pcmOwner.Memory;
                var sampleCount = pcm.Length;
                if (sampleCount <= 0)
                    continue;

                // Apply Opus pre-skip: drop the first _remainingPreSkip samples from decoder output
                if (_remainingPreSkip > 0)
                    if (_remainingPreSkip >= sampleCount) {
                        _remainingPreSkip -= sampleCount;
                        // Entire buffer skipped
                        continue;
                    }

                var skipSampleCount = Math.Min(_remainingPreSkip, sampleCount);
                var playSampleCount = sampleCount - skipSampleCount;
                _decodeBuffer.TryWrite(pcm.Span.Slice(skipSampleCount, playSampleCount));

                // Update played samples and periodically report the playing state
                _remainingPreSkip -= skipSampleCount;
                if (_remainingPreSkip < 0)
                    _remainingPreSkip = 0;
            }
        }
        catch (OperationCanceledException) {
            /* Ignore */
        }
        catch (Exception e) {
            Log.LogError(e, "Decode/Feed loop failed");
            TryReportEnded(e.Message);
        }
        finally {
            Interlocked.Exchange(ref _decodeCompleted, 1);
        }
    }

    private void OnQuantumStarted(AudioFrameInputNode sender, FrameInputNodeQuantumStartedEventArgs args)
    {
        var playSampleCount = args.RequiredSamples;
        var bytes = playSampleCount * sizeof(float);
        using var audioFrame = new AudioFrame((uint)bytes);
        bool hasData;
        unsafe {
            // Lock should be located within scope of the using block to allow AddFrame call to succeed
            using var buffer = audioFrame.LockBuffer(AudioBufferAccessMode.Write);
            using var reference = buffer.CreateReference();
            WindowsRuntimeMarshal.TryGetDataUnsafe(reference, out IntPtr dataPtr, out var capacity);
            if (dataPtr == IntPtr.Zero || capacity < bytes)
                return;

            var dst = new Span<float>((void*)dataPtr, playSampleCount);
            var available = Math.Min(_decodeBuffer.Count, playSampleCount);
            if (available > 0) {
                _decodeBuffer.TryRead(dst[..available], out _); // always succeeds: Count >= available
                if (available < playSampleCount)
                    dst[available..].Clear();
                hasData = true;
            }
            else {
                hasData = false;
                if (Volatile.Read(ref _decodeCompleted) != 0) {
                    _ = End(true, CancellationToken.None);
                    return;
                }
                // Starving - report playing state
                ReportPlaying();
                dst.Clear();
            }
        }
        _frameInput?.AddFrame(audioFrame);
        if (!hasData)
            return;

        // Update played samples and periodically report playing state only when data is available
        _playedSamples += playSampleCount;
        var now = DateTime.UtcNow;
        if ((now - _lastReportAt).TotalMilliseconds >= 200) {
            _lastReportAt = now;
            ReportPlaying();
        }
    }

    private void ReportPlaying()
    {
        // Compute offset in seconds from played samples
        var played = (double)_playedSamples / Constants.Audio.PlaybackSampleRate;
        var buffered = TimeSpan.FromSeconds((double)_decodeBuffer.Count / Constants.Audio.PlaybackSampleRate);
        var isBufferLow = buffered < Constants.Audio.LowPlaybackBufferDuration;
        try {
            playerBackend.OnPlaying(played, _isPaused, isBufferLow);
        }
        catch {
            // Don't propagate reporting errors
        }
        if (!_isPaused && _playedSamples > 0)
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

    private void TryReportEnded(string? message)
    {
        if (Interlocked.Exchange(ref _endedReported, 1) != 0)
            return;

        try {
            playerBackend.OnEnded(message);
        }
        catch {
            // Ignore
        }
    }

    private static TimeSpan GetEncodedBufferDuration(TimeSpan targetBufferSize)
    {
        var encoded = targetBufferSize - Constants.Audio.DecodedBufferSize - Constants.Audio.AudioEnginePlaybackLatency;
        return TimeSpanExt.Max(encoded, Constants.Audio.MinEncodedBufferSize);
    }

    private readonly struct ByteArrayMemoryOwner(byte[] buffer) : IMemoryOwner<byte>
    {
        public Memory<byte> Memory { get; } = buffer;

        public void Dispose()
        { }
    }
}
#endif
