#if WINDOWS
using System.Buffers;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.MediaProperties;
using ActualChat.MediaPlayback;
using ActualChat.Media;
using ActualChat.UI.Blazor.App.Components;
using Microsoft.Extensions.Logging;

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
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });

    private AudioGraph? _graph;
    private AudioDeviceOutputNode? _deviceOutput;
    private AudioFrameInputNode? _frameInput;
    private Task? _decodeTask;
    private volatile bool _started;

    [field: AllowNull, MaybeNull]
    private IAudioCodec AudioCodec => field ??= services.GetRequiredService<IAudioCodec>();

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= services.LogFor<WindowsAudioPlaybackEngine>();

    public async Task Play(CancellationToken cancellationToken)
    {
        if (_started)
            return;

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
        return Task.CompletedTask;
    }

    public Task Resume(CancellationToken cancellationToken)
    {
        if (_frameInput == null)
            throw StandardError.StateTransition(GetType(), "Start command should be called first.");
        _frameInput.Start();
        return Task.CompletedTask;
    }

    public async Task End(bool abort, CancellationToken cancellationToken)
    {
        try { _packetChannel.Writer.TryComplete(); } catch { }
        try { if (_decodeTask != null) await _decodeTask.ConfigureAwait(false); } catch { }

        if (_graph != null) {
            try { _frameInput?.Stop(); _graph.Stop(); } catch { /* ignore */ }
            _frameInput?.Dispose();
            _deviceOutput?.Dispose();
            _graph.Dispose();
        }
        _graph = null; _deviceOutput = null; _frameInput = null; _started = false;
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
                using var _ = pcmOwner;
                var samples = pcmOwner.Memory.Length;
                if (samples <= 0)
                    continue;

                var bytes = samples * sizeof(float);
                using var audioFrame = new AudioFrame((uint)bytes);
                using var buffer = audioFrame.LockBuffer(AudioBufferAccessMode.Write);
                using var reference = buffer.CreateReference();
                unsafe {
                    WindowsRuntimeMarshal.TryGetDataUnsafe(reference, out IntPtr dataPtr, out var capacity);
                    if (dataPtr == IntPtr.Zero || capacity < bytes)
                        continue;

                    var src = pcmOwner.Memory.Span;
                    var dst = new Span<float>((void*)dataPtr, samples);
                    src.CopyTo(dst);
                }
                _frameInput?.AddFrame(audioFrame);
            }
        }
        catch (OperationCanceledException) {
            /* ignore */
        }
        catch (Exception e) {
            Log.LogError(e, "Decode/Feed loop failed");
        }
    }

    private readonly struct PooledSliceOwner<T>(IMemoryOwner<T> rented, int length) : IMemoryOwner<T>
    {
        public Memory<T> Memory => rented.Memory[..length];
        public void Dispose() => rented.Dispose();
    }
}
#endif
