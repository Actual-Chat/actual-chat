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
    // According to Opus spec, the decoder output must skip the first PreSkip samples at 48 kHz
    // We track how many samples remain to be skipped and drop them in the decode loop.
    private int _remainingPreSkip;
    private readonly Channel<IMemoryOwner<byte>> _packetChannel = Channel.CreateUnbounded<IMemoryOwner<byte>>(new UnboundedChannelOptions {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });

    private AudioGraph? _graph;
    private AudioDeviceOutputNode? _deviceOutput;
    private AudioFrameInputNode? _frameInput;
    private Task? _decodeTask;
    private volatile bool _started;

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
        if (_started)
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

        // Start background decode loop
        var ct = cancellationToken;
        _decodeTask = Task.Run(() => DecodeAndFeed(ct), CancellationToken.None);

        try {
            _frameInput.Start();
            _graph.Start();
            _started = true;
            _isPaused = false;
            // Initial report that we're ready to play
            _ = ReportPlaying();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to start playback graph");
            throw;
        }
    }

    public Task Pause(CancellationToken cancellationToken)
    {
        if (_frameInput == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");
        _frameInput.Stop();
        _isPaused = true;
        _ = ReportPlaying();
        return Task.CompletedTask;
    }

    public Task Resume(CancellationToken cancellationToken)
    {
        if (_frameInput == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");
        _frameInput.Start();
        _isPaused = false;
        _ = ReportPlaying();
        return Task.CompletedTask;
    }

    public async Task End(bool abort, CancellationToken cancellationToken)
    {
        try {
            _packetChannel.Writer.TryComplete();
        } catch { /* ignore */  }
        try {
            if (_decodeTask != null)
                await _decodeTask.ConfigureAwait(false);
        } catch { /* ignore */  }

        if (_graph != null) {
            try {
                _frameInput?.Stop(); _graph.Stop();
            } catch { /* ignore */ }
            _frameInput?.Dispose();
            _deviceOutput?.Dispose();
            _graph.Dispose();
        }
        _graph = null; _deviceOutput = null; _frameInput = null; _started = false;
        _isPaused = true;
        // Report end (no error message). If an error already reported, this will no-op.
        TryReportEnded(null);
    }

    public Task Frame(MediaFrame frame, CancellationToken cancellationToken)
    {
        // Enqueue opus packet
        var data = frame.Data;
        if (data.Length == 0)
            return Task.CompletedTask;
        var owner = MemoryPool<byte>.Shared.Rent(data.Length);
        data.AsSpan().CopyTo(owner.Memory.Span);
        if (!_packetChannel.Writer.TryWrite(new PooledSliceOwner<byte>(owner, data.Length)))
            owner.Dispose();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
        => await End(true, CancellationToken.None).ConfigureAwait(false);

    private async Task DecodeAndFeed(CancellationToken cancellationToken)
    {
        try {
            var input = _packetChannel.Reader.ReadAllAsync(cancellationToken);
            await foreach (var pcmOwner in AudioCodec.Decode(input, cancellationToken).ConfigureAwait(false)) {
                using var pcm = pcmOwner;
                var samples = pcmOwner.Memory.Length;
                if (samples <= 0)
                    continue;

                // Apply Opus pre-skip: drop the first _remainingPreSkip samples from decoder output
                if (_remainingPreSkip > 0) {
                    if (_remainingPreSkip >= samples) {
                        _remainingPreSkip -= samples;
                        // Entire buffer skipped
                        continue;
                    }
                }

                var skip = Math.Min(_remainingPreSkip, samples);
                var playSamples = samples - skip;
                var bytes = playSamples * sizeof(float);
                using var audioFrame = new AudioFrame((uint)bytes);
                using var buffer = audioFrame.LockBuffer(AudioBufferAccessMode.Write);
                using var reference = buffer.CreateReference();
                unsafe {
                    WindowsRuntimeMarshal.TryGetDataUnsafe(reference, out IntPtr dataPtr, out var capacity);
                    if (dataPtr == IntPtr.Zero || capacity < bytes)
                        continue;

                    var src = pcmOwner.Memory.Span.Slice(skip, playSamples);
                    var dst = new Span<float>((void*)dataPtr, playSamples);
                    src.CopyTo(dst);
                }
                _frameInput?.AddFrame(audioFrame);

                // Update played samples and periodically report playing state
                _remainingPreSkip -= skip;
                if (_remainingPreSkip < 0) _remainingPreSkip = 0;
                _playedSamples += playSamples;
                var now = DateTime.UtcNow;
                if ((now - _lastReportAt).TotalMilliseconds >= 200) {
                    _lastReportAt = now;
                    _ = ReportPlaying();
                }
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

    private Task ReportPlaying()
    {
        // Compute offset in seconds from played samples
        var seconds = (double)_playedSamples / Constants.Audio.PlaybackSampleRate;
        var isBufferLow = !_isPaused; // allow feeding while playing; block when paused
        try {
            return playerBackend.OnPlaying(seconds, _isPaused, isBufferLow);
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

    private readonly struct PooledSliceOwner<T>(IMemoryOwner<T> rented, int length) : IMemoryOwner<T>
    {
        public Memory<T> Memory => rented.Memory[..length];
        public void Dispose() => rented.Dispose();
    }
}
#endif
