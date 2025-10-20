using System.Buffers;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using ActualChat.App.Maui.Audio.APM;
using ActualChat.App.Maui.Services.Recording;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Role = NAudio.CoreAudioApi.Role;

namespace ActualChat.App.Maui.Audio;

public class WindowsAudioCapture(ILogger<WindowsAudioCapture> log) : IAudioCapture
{
    private const int NAudioCaptureBufferMs = 20;
    public ILogger<WindowsAudioCapture> Log { get; } = log;

    public async Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken)
    {
        var apm = new AudioProcessingModule(
            new StreamConfig(Constants.Audio.RecordingSampleRate, Constants.Audio.Channels),
            new StreamConfig(Constants.Audio.PlaybackSampleRate, Constants.Audio.Channels));

        try {
            apm.SetDelay(50);
            apm.Configure(cfg => cfg
                .EnableEchoCanceller(true)
                .EnableNoiseSuppression(true, NoiseSuppressionLevel.Moderate)
                .EnableAutomaticGainControl(true)
                .EnableHighPassFilter(true)
                .SetPipeline(false, false));
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to configure AudioProcessingModule; proceeding without APM features");
        }

        // Desired input format: LPCM float32 mono for microphone
        var micEncoding = AudioEncodingProperties.CreatePcm(
            Constants.Audio.RecordingSampleRate,
            Constants.Audio.Channels,
            32); // 32-bit for float
        micEncoding.Subtype = MediaEncodingSubtypes.Float;
        var settings = new AudioGraphSettings(AudioRenderCategory.Communications) {
            // Let AudioGraph choose optimal processing; we're only capturing
            QuantumSizeSelectionMode = QuantumSizeSelectionMode.ClosestToDesired,
            DesiredSamplesPerQuantum = Constants.Audio.RecordingSampleRate / 1000 * Constants.Audio.OpusFrameLength,
        };

        // Create NAudio captures for loopback
        WasapiCapture? loopbackCapture = null;

        var channel = Channel.CreateUnbounded<IMemoryOwner<float>>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

        var microphoneRingBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);
        var loopbackRingBuffer = new BlockRingBuffer<float>(Constants.Audio.PlaybackSampleRate * 10);

        var micApmFrameSize = Constants.Audio.RecordingSampleRate / 1000 * Constants.Audio.ApmFrameDurationMs * Constants.Audio.Channels;
        var loopApmFrameSize = Constants.Audio.PlaybackSampleRate / 1000 * Constants.Audio.ApmFrameDurationMs * Constants.Audio.Channels;

        var graphCreate = await AudioGraph.CreateAsync(settings).AsTask(cancellationToken);
        if (graphCreate.Status != AudioGraphCreationStatus.Success || graphCreate.Graph is null) {
            apm.DisposeSilently();
            throw new InvalidOperationException($"AudioGraph creation failed: {graphCreate.Status}");
        }

        var graph = graphCreate.Graph;

        var inputCreate = await graph
            .CreateDeviceInputNodeAsync(MediaCategory.Communications, micEncoding)
            .AsTask(cancellationToken);
        if (inputCreate.Status != AudioDeviceNodeCreationStatus.Success || inputCreate.DeviceInputNode is null) {
            // Unable to get microphone stream
            graph.DisposeSilently();
            apm.DisposeSilently();
            return null;
        }

        var inputNode = inputCreate.DeviceInputNode;

        // Frame output for microphone
        var outputNode = graph.CreateFrameOutputNode(micEncoding);
        inputNode.AddOutgoingConnection(outputNode);

        graph.QuantumStarted += QuantumEventHandler;

        try {
            // Default communications microphone
            var devices = new MMDeviceEnumerator();
            var loopbackDevice = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            loopbackCapture = new CustomWasapiLoopbackCapture(loopbackDevice, true, NAudioCaptureBufferMs);
            loopbackCapture.DataAvailable += OnLoopbackCaptureOnDataAvailable;
            loopbackCapture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Constants.Audio.PlaybackSampleRate, 1);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize NAudio capture devices");
            apm.DisposeSilently();
            inputNode.DisposeSilently();
            graph.DisposeSilently();
            loopbackCapture.DisposeSilently();
            return null;
        }

        // Processing loop to emit mic frames through APM
        var processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var processingToken = processingCts.Token;
        var processingTask = BackgroundTask.Run(async () => {
            try {
                using var emptyBuffer = MemoryPool<float>.Shared.Rent(loopApmFrameSize);
                emptyBuffer.Memory.Span.Fill(0);

                while (!processingToken.IsCancellationRequested) {
                    if (!microphoneRingBuffer.TryPull(micApmFrameSize, out var micBlock)) {
                        await microphoneRingBuffer.WhenPushed.WaitAsync(processingToken).ConfigureAwait(false);
                        var expectedLoopbackFrames = 2 * loopApmFrameSize;
                        if (loopbackRingBuffer.Count > expectedLoopbackFrames) {
                            // Skip loopback frames that are too old
                            var framesToSkip = loopbackRingBuffer.Count - expectedLoopbackFrames;
                            if (loopbackRingBuffer.TryPull(framesToSkip, out var block))
                                block.Dispose();
                        }
                        continue;
                    }
                    loopbackRingBuffer.TryPull(loopApmFrameSize, out var loopBlock);
                    using var _ = micBlock;
                    using var __ = loopBlock;
                    var micIn = micBlock.Memory.Span;
                    var outOwner = MemoryPool<float>.Shared.Rent(micApmFrameSize);
                    var outSpan = outOwner.Memory.Span[..micApmFrameSize];
                    try {
                        var loopIn = loopBlock is not null
                            ? loopBlock.Memory.Span[..loopApmFrameSize]
                            : emptyBuffer.Memory.Span[..loopApmFrameSize];
                        apm.AnalyzeReverseStream(loopIn);
                        apm.ProcessStream(micIn, outSpan);
                        // Log.LogDebug("APM.ProcessStream gains for mic and loopback: {GainMic} {GainLoop}", AudioExt.ApproximateGain(micIn), AudioExt.ApproximateGain(loopIn));
                    }
                    catch (Exception apmEx) {
                        Log.LogDebug(apmEx,
                            "APM.ProcessStream failed; passing through raw audio for this frame");
                        micIn.CopyTo(outSpan);
                    }
                    if (!channel.Writer.TryWrite(new BufferReference(outOwner.Memory[..micApmFrameSize], outOwner)))
                        outOwner.Dispose();
                }
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex) {
                Log.LogError(ex, "Mic processing loop failed");
            }
        }, processingCts.Token);

        // Start recording
        try {
            loopbackCapture.StartRecording();
            graph.Start();
            inputNode.Start();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to start NAudio recording");
            processingCts.CancelAndDisposeSilently();
            apm.DisposeSilently();
            graph.DisposeSilently();
            loopbackCapture.DisposeSilently();
            return null;
        }

        // Return async iterator
        var enumerateToken = cancellationToken;
        return Enumerate(enumerateToken);

        async IAsyncEnumerable<IMemoryOwner<float>> Enumerate([EnumeratorCancellation] CancellationToken ct)
        {
            try {
                await foreach (var block in channel.Reader.ReadAllAsync(ct).SuppressCancellation(ct).ConfigureAwait(false))
                    yield return block;
            }
            finally {
                channel.Writer.TryComplete();
                // Stop and cleanup
                try {
                    inputNode?.Stop();
                    outputNode?.Stop();
                    graph?.Stop();
                    loopbackCapture?.StopRecording();
                }
                catch { /* ignore */ }

                await processingCts.CancelAsync().ConfigureAwait(false);
                try { await processingTask.ConfigureAwait(false); } catch { /* ignore */ }

                apm.DisposeSilently();
                inputNode?.DisposeSilently();
                outputNode?.DisposeSilently();
                graph?.DisposeSilently();
                loopbackCapture?.Dispose();
            }
        }

        void QuantumEventHandler(AudioGraph sender, object args)
        {
            // Quantum callback: pull frames and push decoded float32 mono samples processed by APM (AEC+AGC)
            if (cancellationToken.IsCancellationRequested)
                return;

            try {
                // 2) Pull microphone frame and process through APM
                using var frame = outputNode!.GetFrame();
                if (frame is null)
                    return;

                using var buffer = frame.LockBuffer(AudioBufferAccessMode.Read);
                using var reference = buffer.CreateReference();
                if (reference is null)
                    return;

                unsafe {
                    WindowsRuntimeMarshal.TryGetDataUnsafe(reference, out IntPtr dataPtr, out var capacity);
                    if (dataPtr == IntPtr.Zero || capacity == 0)
                        return;

                    var floatCount = (int)capacity / sizeof(float);
                    var inSpan = new ReadOnlySpan<float>((void*)dataPtr, floatCount);
                    microphoneRingBuffer.TryPush(inSpan);
                }
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to process audio frame");
                // Best effort: ignore frame-level issues
            }
        }

        void OnLoopbackCaptureOnDataAvailable(object? _, WaveInEventArgs args)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (args.BytesRecorded == 0)
                return;

            PushToBuffer(args, loopbackCapture.WaveFormat, loopbackRingBuffer);
        }
    }

    private static void PushToBuffer(WaveInEventArgs args, WaveFormat format, BlockRingBuffer<float> ringBuffer)
    {
        var buffer = args.Buffer;
        var bytesRecorded = args.BytesRecorded;
        if (bytesRecorded <= 0)
            return;

        if (format is { Encoding: WaveFormatEncoding.IeeeFloat, BitsPerSample: 32 }) {
            var floatCount = bytesRecorded / sizeof(float);
            unsafe {
                fixed (byte* b = buffer) {
                    var src = new ReadOnlySpan<float>(b, floatCount);
                    _ = ringBuffer.TryPush(src);
                }
            }
        }
        else if (format.Encoding is WaveFormatEncoding.Pcm && format.BitsPerSample == 16) {
            var sampleCount = bytesRecorded / 2;
            Span<float> tmp = sampleCount <= 4096 ? stackalloc float[sampleCount] : new float[sampleCount];
            for (int i = 0, o = 0; i < bytesRecorded; i += 2, o++) {
                short s = (short)(buffer[i] | (buffer[i + 1] << 8));
                tmp[o] = s / 32768f;
            }
            _ = ringBuffer.TryPush(tmp);
        }
        else {
            // Unsupported format: best effort - treat as bytes and skip
            // In practice, most devices provide 16-bit PCM or 32-bit float
        }
    }

    private readonly struct BufferReference(Memory<float> memory, IMemoryOwner<float> owner) : IMemoryOwner<float>
    {
        public Memory<float> Memory { get; } = memory;
        public void Dispose() => owner.Dispose();
    }

}
