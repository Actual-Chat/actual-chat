using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using Windows.Foundation;
using ActualChat.App.Maui.Services.Recording;
using WinRT;

namespace ActualChat.App.Maui.Recording;

public class WindowsAudioCapture(ILogger<WindowsAudioCapture> log) : IAudioCapture
{
    public ILogger<WindowsAudioCapture> Log { get; } = log;

    public async Task<IAsyncEnumerable<ReadOnlyMemory<float>>?> Capture(CancellationToken cancellationToken)
    {
        // Desired input format: LPCM float32 mono
        var encoding = AudioEncodingProperties.CreatePcm(
            Constants.Audio.RecordingSampleRate,
            Constants.Audio.Channels,
            32); // 32-bit for float
        encoding.Subtype = MediaEncodingSubtypes.Float;

        var settings = new AudioGraphSettings(AudioRenderCategory.Communications) {
            // Let AudioGraph choose optimal processing; we're only capturing
            QuantumSizeSelectionMode = QuantumSizeSelectionMode.ClosestToDesired,
            DesiredSamplesPerQuantum = Constants.Audio.RecordingSampleRate / 1000 * Constants.Audio.VadFrameDurationMs,
        };

        var graphCreate = await AudioGraph.CreateAsync(settings).AsTask(cancellationToken);
        if (graphCreate.Status != AudioGraphCreationStatus.Success || graphCreate.Graph is null)
            throw new InvalidOperationException($"AudioGraph creation failed: {graphCreate.Status}");

        var graph = graphCreate.Graph;

        var inputCreate = await graph
            .CreateDeviceInputNodeAsync(MediaCategory.Communications, encoding)
            .AsTask(cancellationToken);
        if (inputCreate.Status != AudioDeviceNodeCreationStatus.Success || inputCreate.DeviceInputNode is null) {
            // Unable to get microphone stream
            try { graph.DisposeSilently(); } catch { /* ignore */ }
            return null;
        }

        var inputNode = inputCreate.DeviceInputNode;

        // Frame output in the same format
        var outputNode = graph.CreateFrameOutputNode(encoding);
        inputNode.AddOutgoingConnection(outputNode);

        var channel = Channel.CreateUnbounded<Memory<float>>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });

        // Quantum callback: pull frames and push decoded float32 mono samples
        graph.QuantumProcessed += (sender, args) => {
            if (cancellationToken.IsCancellationRequested)
                return;

            try {
                using var frame = outputNode!.GetFrame();
                if (frame is null)
                    return;

                using var buffer = frame.LockBuffer(AudioBufferAccessMode.Read);
                using var reference = buffer.CreateReference();

                if (reference is null)
                    return;

                unsafe {
                    // Access raw bytes
                    WindowsRuntimeMarshal.TryGetDataUnsafe(reference, out IntPtr dataPtr, out var capacity);
                    if (dataPtr == IntPtr.Zero || capacity == 0)
                        return;

                    var floatCount = capacity / sizeof(float);

                    // TODO(AK): optimize and reuse some buffers here
                    var managed = new float[floatCount];

                    // Copy unmanaged bytes to managed float[]
                    Buffer.MemoryCopy((void*)dataPtr,
                        Unsafe.AsPointer(ref managed[0]),
                        (long)floatCount * sizeof(float),
                        capacity);

                    // Enqueue; ownership passed to consumer
                    channel.Writer.TryWrite(managed);
                }
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to process audio frame");
                // Best effort: ignore frame-level issues
            }
        };

        try {
            outputNode.Start();
            graph.Start();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to start audio graph");
            return null;
        }

        // Return async iterator as a nested method
        return Enumerate(cancellationToken);

        async IAsyncEnumerable<ReadOnlyMemory<float>> Enumerate([EnumeratorCancellation] CancellationToken ct)
        {
            await using var ctr = ct.Register(() => {
                try {
                    graph?.Stop();
                }
                catch {
                    /* ignore */
                }

                channel.Writer.TryComplete();
            });

            try {
                await foreach (var block in channel.Reader.ReadAllAsync(ct).SuppressCancellation(ct).ConfigureAwait(false))
                    yield return block;
            }
            finally {
                try {
                    outputNode?.Stop();
                    graph.Stop();
                }
                catch {
                    /* ignore */
                }

                // Dispose nodes and graph
                inputNode.DisposeSilently();
                outputNode.DisposeSilently();
                graph.DisposeSilently();
            }
        }
    }
}
