using System.Buffers;
using ActualChat.App.Maui.Audio.APM;
using ActualChat.App.Maui.Services.Recording;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace ActualChat.App.Maui.Audio;

public class WindowsAudioCapture(ILogger<WindowsAudioCapture> log) : IAudioCapture
{
    public ILogger<WindowsAudioCapture> Log { get; } = log;

    public Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken)
    {
        var apm = new AudioProcessingModule(
            new StreamConfig(Constants.Audio.RecordingSampleRate, Constants.Audio.Channels),
            new StreamConfig(Constants.Audio.PlaybackSampleRate, Constants.Audio.Channels));

        try {
            apm.Configure(cfg => cfg
                .EnableEchoCanceller(true)
                .EnableNoiseSuppression(true, NoiseSuppressionLevel.High)
                .EnableAutomaticGainControl(true)
                .EnableHighPassFilter(true)
                .SetPipeline(false, false));
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to configure AudioProcessingModule; proceeding without APM features");
        }

        // Create NAudio captures for mic and loopback
        WasapiCapture? micCapture = null;
        WasapiLoopbackCapture? loopbackCapture = null;

        var channel = Channel.CreateUnbounded<IMemoryOwner<float>>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

        var microphoneRingBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);
        var loopbackRingBuffer = new BlockRingBuffer<float>(Constants.Audio.PlaybackSampleRate * 10);

        var micApmFrameSize = Constants.Audio.RecordingSampleRate / 1000 * Constants.Audio.VadFrameDurationMs * Constants.Audio.Channels;
        var loopApmFrameSize = Constants.Audio.PlaybackSampleRate / 1000 * Constants.Audio.VadFrameDurationMs * Constants.Audio.Channels;

        try {
            // Default communications microphone
            var devices = new MMDeviceEnumerator();
            var micDevice = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            micCapture = new WasapiCapture(micDevice, true, 20);
            micCapture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Constants.Audio.RecordingSampleRate, 1);
            micCapture.DataAvailable += OnMicCaptureOnDataAvailable;

            loopbackCapture = new WasapiLoopbackCapture();
            loopbackCapture.DataAvailable += OnLoopbackCaptureOnDataAvailable;
            loopbackCapture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Constants.Audio.PlaybackSampleRate, 1);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize NAudio capture devices");
            apm.DisposeSilently();
            micCapture.DisposeSilently();
            loopbackCapture.DisposeSilently();
            return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(null);
        }

        // Processing loop to emit mic frames through APM
        var processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var processingToken = processingCts.Token;
        var processingTask = BackgroundTask.Run(async () => {
            try {
                while (!processingToken.IsCancellationRequested) {
                    if (!microphoneRingBuffer.TryPull(micApmFrameSize, out var micBlock)) {
                        await microphoneRingBuffer.WhenPushed.WaitAsync(processingToken).ConfigureAwait(false);
                        continue;
                    }
                    if (!loopbackRingBuffer.TryPull(loopApmFrameSize, out var loopBlock)) {
                        await loopbackRingBuffer.WhenPushed.WaitAsync(processingToken).ConfigureAwait(false);
                        continue;
                    }

                    using var _ = micBlock;
                    using var __ = loopBlock;
                    var micIn = micBlock.Memory.Span;
                    var loopIn = loopBlock.Memory.Span;
                    var outOwner = MemoryPool<float>.Shared.Rent(micApmFrameSize);
                    var outSpan = outOwner.Memory.Span[..micApmFrameSize];
                    try {
                        apm.AnalyzeReverseStream(loopIn);
                        apm.ProcessStream(micIn, outSpan);
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
            micCapture.StartRecording();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to start NAudio recording");
            processingCts.Cancel();
            apm.DisposeSilently();
            micCapture.DisposeSilently();
            loopbackCapture.DisposeSilently();
            return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(null);
        }

        // Return async iterator
        var enumerateToken = cancellationToken;
        return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(Enumerate(enumerateToken));

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
                    micCapture?.StopRecording();
                    loopbackCapture?.StopRecording();
                }
                catch { /* ignore */ }

                await processingCts.CancelAsync().ConfigureAwait(false);
                try { await processingTask.ConfigureAwait(false); } catch { /* ignore */ }

                apm.DisposeSilently();
                micCapture?.Dispose();
                loopbackCapture?.Dispose();
            }
        }

        void OnMicCaptureOnDataAvailable(object? _, WaveInEventArgs args)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            PushToBuffer(args, micCapture.WaveFormat, microphoneRingBuffer);
        }

        void OnLoopbackCaptureOnDataAvailable(object? _, WaveInEventArgs args)
        {
            if (cancellationToken.IsCancellationRequested)
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
