using System.Buffers;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.App.Maui.Audio.APM;

namespace ActualChat.App.Maui.Audio;

public class WindowsAudioCapture(ILogger<WindowsAudioCapture> log) : IAudioCapture
{
    public ILogger<WindowsAudioCapture> Log { get; } = log;

    public async Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken)
    {
        var apm = new AudioProcessingModule(
            new StreamConfig(Constants.Audio.RecordingSampleRate, Constants.Audio.Channels),
            new StreamConfig(Constants.Audio.PlaybackSampleRate, Constants.Audio.Channels));

        // Enable AEC, NS, and AGC in WebRTC APM
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

        // Desired input format: LPCM float32 mono for microphone
        var micEncoding = AudioEncodingProperties.CreatePcm(
            Constants.Audio.RecordingSampleRate,
            Constants.Audio.Channels,
            32); // 32-bit for float
        micEncoding.Subtype = MediaEncodingSubtypes.Float;

        // Desired render/loopback format: LPCM float32 mono for playback
        var renderEncoding = AudioEncodingProperties.CreatePcm(
            Constants.Audio.PlaybackSampleRate,
            Constants.Audio.Channels,
            32);
        renderEncoding.Subtype = MediaEncodingSubtypes.Float;

        var settings = new AudioGraphSettings(AudioRenderCategory.Communications) {
            // Let AudioGraph choose optimal processing; we're only capturing
            QuantumSizeSelectionMode = QuantumSizeSelectionMode.ClosestToDesired,
            DesiredSamplesPerQuantum = Constants.Audio.RecordingSampleRate / 1000 * Constants.Audio.VadFrameDurationMs,
        };

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

        // Try to create loopback (render) capture node -> frame output node
        AudioDeviceInputNode? loopbackInputNode = null;
        AudioFrameOutputNode? loopbackOutputNode = null;
        try {
            var renderId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
            var renderDeviceInfo = await DeviceInformation.CreateFromIdAsync(renderId).AsTask(cancellationToken);
            var loopbackCreate = await graph.CreateDeviceInputNodeAsync(MediaCategory.Other, renderEncoding, renderDeviceInfo).AsTask(cancellationToken);
            if (loopbackCreate.Status == AudioDeviceNodeCreationStatus.Success && loopbackCreate.DeviceInputNode is not null) {
                loopbackInputNode = loopbackCreate.DeviceInputNode;
                loopbackOutputNode = graph.CreateFrameOutputNode(renderEncoding);
                loopbackInputNode.AddOutgoingConnection(loopbackOutputNode);
            }
            else
                Log.LogWarning("Loopback (render) capture node creation failed: {Status}", loopbackCreate.Status);
        }
        catch (Exception ex) {
            Log.LogWarning(ex, "Loopback (render) capture initialization failed; AEC will operate without reverse stream");
        }

        var channel = Channel.CreateUnbounded<IMemoryOwner<float>>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });

        graph.QuantumStarted += QuantumEventHandler;

        try {
            outputNode.Start();
            loopbackOutputNode?.Start();
            graph.Start();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to start audio graph");
            apm.DisposeSilently();
            graph.DisposeSilently();
            return null;
        }

        // Return async iterator as a nested method
        return Enumerate(cancellationToken);

        async IAsyncEnumerable<IMemoryOwner<float>> Enumerate([EnumeratorCancellation] CancellationToken ct)
        {
            try {
                await foreach (var block in channel.Reader.ReadAllAsync(ct).SuppressCancellation(ct).ConfigureAwait(false))
                    yield return block;
            }
            finally {
                channel.Writer.TryComplete();

                // Stop graph and detach event handler
                graph.QuantumStarted -= QuantumEventHandler;
                try {
                    outputNode?.Stop();
                    loopbackOutputNode?.Stop();
                    graph.Stop();
                }
                catch {
                    /* ignore */
                }

                // Dispose nodes and graph
                apm.DisposeSilently();
                inputNode.DisposeSilently();
                outputNode.DisposeSilently();
                loopbackInputNode?.DisposeSilently();
                loopbackOutputNode?.DisposeSilently();
                graph.DisposeSilently();
            }
        }

        void QuantumEventHandler(AudioGraph sender, object args)
        {
            // Quantum callback: pull frames and push decoded float32 mono samples processed by APM (AEC+AGC)
            if (cancellationToken.IsCancellationRequested)
                return;

            try {
                // 1) Pull loopback/render frame (if available) and feed to APM reverse analysis
                if (loopbackOutputNode is not null) {
                    using var rFrame = loopbackOutputNode.GetFrame();
                    if (rFrame is not null) {
                        using var rBuffer = rFrame.LockBuffer(AudioBufferAccessMode.Read);
                        using var rRef = rBuffer.CreateReference();
                        if (rRef is not null) {
                            unsafe {
                                WindowsRuntimeMarshal.TryGetDataUnsafe(rRef, out IntPtr rPtr, out var rCapacity);
                                if (rPtr != IntPtr.Zero && rCapacity > 0) {
                                    var rFloatCount = (int)rCapacity / sizeof(float);
                                    // Feed whatever we have; APM will segment internally as needed
                                    var rSpan = new ReadOnlySpan<float>((void*)rPtr, rFloatCount);
                                    try {
                                        apm.AnalyzeReverseStream(rSpan);
                                    }
                                    catch (Exception ex) {
                                        // Reverse analysis issues should not break capture
                                        Log.LogDebug(ex, "APM.AnalyzeReverseStream failed; continuing without reverse for this quantum");
                                    }
                                }
                            }
                        }
                    }
                }

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

                    // Rent buffers for input and processed output
                    var ownerOut = MemoryPool<float>.Shared.Rent(floatCount);
                    try {
                        var inSpan = new ReadOnlySpan<float>((void*)dataPtr, floatCount);
                        var outSpan = ownerOut.Memory.Span[..floatCount];

                        try {
                            apm.ProcessStream(inSpan, outSpan);
                        }
                        catch (Exception apmEx) {
                            // On failure, pass-through
                            Log.LogDebug(apmEx, "APM.ProcessStream failed; passing through raw audio for this quantum");
                            inSpan.CopyTo(outSpan);
                        }

                        // Enqueue processed block; ownership passed to consumer
                        if (!channel.Writer.TryWrite(new BufferReference(ownerOut.Memory[..floatCount], ownerOut)))
                            ownerOut.Dispose();
                    }
                    catch {
                        ownerOut.Dispose();
                        throw;
                    }
                }
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to process audio frame");
                // Best effort: ignore frame-level issues
            }
        }
    }

    private readonly struct BufferReference(Memory<float> memory, IMemoryOwner<float> owner): IMemoryOwner<float>
    {
        public Memory<float> Memory { get; } = memory;
        public void Dispose() => owner.Dispose();
    }
}
