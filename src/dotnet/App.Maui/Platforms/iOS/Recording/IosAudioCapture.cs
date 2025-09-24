using System.Buffers;
using ActualChat.App.Maui.Services.Recording;
using AVFoundation;

namespace ActualChat.App.Maui.Recording;

public class IosAudioCapture(ILogger<IosAudioCapture> log) : IAudioCapture
{
    public ILogger<IosAudioCapture> Log { get; } = log;

    public Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken)
    {
        // Configure AVAudioEngine to capture float32 PCM mono at desired sample rate
        var sampleRate = Constants.Audio.RecordingSampleRate;
        var frameSamples = Constants.Audio.OpusFrameLength; // 20 ms at 16 kHz = 320 samples
        var channel = Channel.CreateUnbounded<IMemoryOwner<float>>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

        AVAudioEngine? engine = null;
        AVAudioInputNode? inputNode = null;

        try {
            engine = new AVAudioEngine();
            inputNode = engine.InputNode;
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (inputNode is null)
                return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(null);

        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize iOS audio capture");
            engine?.DisposeSilently();
            return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(null);
        }
        // Float32 mono
        var format = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, sampleRate, 1, interleaved: false);

        // Install a tap to receive buffers of approximately frameSamples frames
        inputNode.InstallTapOnBus(0, (uint)frameSamples, format, (buffer, when) =>
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (buffer is null || cancellationToken.IsCancellationRequested)
                return;

            try {
                var frames = (int)buffer.FrameLength;
                if (frames <= 0)
                    return;

                var owner = MemoryPool<float>.Shared.Rent(frames);
                try {
                    var dst = owner.Memory.Span[..frames];

                    unsafe {
                        var dataPtr = buffer.FloatChannelData;
                        if (dataPtr == IntPtr.Zero) {
                            owner.Dispose();
                            return;
                        }

                        // For mono input, FloatChannelData points to channel 0 data
                        var src = new ReadOnlySpan<float>((void*)dataPtr, frames);
                        src.CopyTo(dst);
                    }

                    if (!channel.Writer.TryWrite(new BufferReference(owner.Memory[..frames], owner)))
                        owner.Dispose();
                }
                catch {
                    owner.Dispose();
                    throw;
                }
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to process iOS audio buffer");
            }
        });

        engine.Prepare();
        var started = engine.StartAndReturnError(out var nsError);
        if (started)
            return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(Enumerate(cancellationToken));

        Log.LogError("Unable to start recording: {Error}", nsError?.LocalizedDescription ?? "Unknown error");
        try {
            inputNode.RemoveTapOnBus(0);
        }
        catch {
            /* ignore */
        }
        engine.Dispose();
        return Task.FromResult<IAsyncEnumerable<IMemoryOwner<float>>?>(null);

        async IAsyncEnumerable<IMemoryOwner<float>> Enumerate([EnumeratorCancellation] CancellationToken ct)
        {
            try {
                await foreach (var block in channel.Reader.ReadAllAsync(ct).SuppressCancellation(ct).ConfigureAwait(false))
                    yield return block;
            }
            finally {
                channel.Writer.TryComplete();

                try {
                    inputNode?.RemoveTapOnBus(0);
                }
                catch {
                    /* ignore */
                }
                try {
                    if (engine?.Running == true)
                        engine.Stop();
                } catch { /* ignore */ }

                engine.DisposeSilently();
            }
        }
    }

    private readonly struct BufferReference(Memory<float> memory, IMemoryOwner<float> owner) : IMemoryOwner<float>
    {
        public Memory<float> Memory { get; } = memory;
        public void Dispose() => owner.Dispose();
    }
}
