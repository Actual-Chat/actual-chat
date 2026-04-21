using System.Buffers;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.Audio.APM;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Role = NAudio.CoreAudioApi.Role;

namespace ActualChat.App.Maui.Audio;

public class WindowsAudioCapture(ILogger<WindowsAudioCapture> log) : IAudioCapture
{
    private const int CaptureBufferMs = Constants.Audio.ApmFrameDurationMs;
    private const int MicDelaySamples = Constants.Audio.RecordingSampleRate * 40 / 1000; // 40ms delay
    public ILogger<WindowsAudioCapture> Log { get; } = log;

    public async Task<IAsyncEnumerable<IMemoryOwner<float>>?> Capture(CancellationToken cancellationToken)
    {
        var apm = new AudioProcessingModule(
            new StreamConfig(Constants.Audio.RecordingSampleRate, Constants.Audio.Channels),
            new StreamConfig(Constants.Audio.RecordingSampleRate, Constants.Audio.Channels));

        try {
            // apm.SetDelay(40);
            apm.Configure(cfg => cfg
                .EnableEchoCanceller(true)
                .EnableNoiseSuppression(true, NoiseSuppressionLevel.Moderate)
                .EnableAutomaticGainControl(true)
                .EnableHighPassFilter(true)
                .SetPipeline(false, false));
            apm.SetDelay(MicDelaySamples);
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
        var settings = new AudioGraphSettings(AudioRenderCategory.Other) {
            // Use a non-communications category to avoid OS voice processing (AEC/NS/AGC) on capture
            QuantumSizeSelectionMode = QuantumSizeSelectionMode.ClosestToDesired,
            DesiredSamplesPerQuantum = Constants.Audio.RecordingSampleRate / 1000 * CaptureBufferMs,
        };

        // Create NAudio captures for loopback
        WasapiCapture? loopbackCapture = null;
        MMDevice? micDevice = null;

        var outputBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);
        var microphoneBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);
        var loopbackBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);

        const int apmFrameSize = Constants.Audio.RecordingSampleRate
            / 1000
            * Constants.Audio.ApmFrameDurationMs
            * Constants.Audio.Channels;

        var graphCreate = await AudioGraph.CreateAsync(settings).AsTask(cancellationToken).ConfigureAwait(true);
        if (graphCreate.Status != AudioGraphCreationStatus.Success || graphCreate.Graph is null) {
            apm.DisposeSilently();
            throw new InvalidOperationException($"AudioGraph creation failed: {graphCreate.Status}");
        }

        var graph = graphCreate.Graph;

        var inputCreate = await graph
            .CreateDeviceInputNodeAsync(MediaCategory.Other, micEncoding)
            .AsTask(cancellationToken)
            .ConfigureAwait(true);
        if (inputCreate.Status != AudioDeviceNodeCreationStatus.Success || inputCreate.DeviceInputNode is null) {
            // Unable to get microphone stream
            graph.DisposeSilently();
            apm.DisposeSilently();
            return null;
        }

        var inputNode = inputCreate.DeviceInputNode;

        // Ensure no built-in audio effects are applied on the capture path
        inputNode.EffectDefinitions.Clear();

        // Frame output for microphone
        var outputNode = graph.CreateFrameOutputNode(micEncoding);
        inputNode.AddOutgoingConnection(outputNode);

        graph.QuantumStarted += QuantumEventHandler;

        try {
            // Default communications microphone and loopback render device
            var devices = new MMDeviceEnumerator();
            micDevice = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            var loopbackDevice = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            loopbackCapture = new CustomWasapiLoopbackCapture(loopbackDevice, true, CaptureBufferMs);
            loopbackCapture.DataAvailable += OnLoopbackCaptureOnDataAvailable;
            loopbackCapture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Constants.Audio.RecordingSampleRate, 1);
        }
        catch (Exception e) {
            // Under NativeAOT, ILC emits an invalid body for NAudio's COM ComObject wrapper
            // ctor (InvalidProgramException on MMDeviceEnumeratorComObject..ctor()).
            // TrimmerRootAssembly on NAudio.Wasapi does not fix it. Same pattern as the
            // WindowConfigurator / WindowsAppIconBadge known issues: wrap and degrade.
            // The mic itself goes through WinRT AudioGraph above (AOT-safe), so we continue
            // without the AEC loopback reverse stream and without mic volume control.
            Log.LogWarning(e, "NAudio capture devices unavailable; continuing without AEC loopback and mic volume control");
            loopbackCapture.DisposeSilently();
            loopbackCapture = null;
            micDevice.DisposeSilently();
            micDevice = null;
        }

        // Throttled access to microphone volume to avoid per-frame COM calls
        var endpointVolume = micDevice?.AudioEndpointVolume;
        var currentVolume = Math.Clamp(endpointVolume?.MasterVolumeLevelScalar ?? 0.5f, 0f, 1f);
        endpointVolume?.OnVolumeNotification += OnVolumeChanged;

        // Processing loop to emit mic frames through APM
        var processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var processingToken = processingCts.Token;
        var processingTask = BackgroundTask.Run(async () => {
            var emptyArray = ArrayPools.SharedFloatPool.Rent(apmFrameSize);
            var emptyMemory = emptyArray.AsMemory(0, apmFrameSize);
            emptyMemory.Span.Clear();
            try {
                while (!processingToken.IsCancellationRequested) {
                    // Check if we have enough microphone samples to enforce the delay
                    if (microphoneBuffer.Count < apmFrameSize + MicDelaySamples) {
                        var whenReady = microphoneBuffer.WhenReadyToRead();
                        if (whenReady != null)
                            await whenReady.WaitAsync(processingToken).ConfigureAwait(false);
                        else
                            await Task.Delay(1, processingToken).ConfigureAwait(false);
                        continue;
                    }

                    // Pull delayed microphone data
                    var micArray = ArrayPools.SharedFloatPool.Rent(apmFrameSize);
                    var micIn = micArray.AsSpan(0, apmFrameSize);
                    if (!microphoneBuffer.TryRead(micIn, out var micWhenReady)) {
                        ArrayPools.SharedFloatPool.Return(micArray);
                        await micWhenReady.WaitAsync(processingToken).ConfigureAwait(false);
                        continue;
                    }

                    var outArray = ArrayPools.SharedFloatPool.Rent(apmFrameSize);
                    var outSpan = outArray.AsSpan(0, apmFrameSize);

                    // Pull loopback data (use the most recent or zeros)
                    var loopArray = ArrayPools.SharedFloatPool.Rent(apmFrameSize);
                    var loopSpan = loopArray.AsSpan(0, apmFrameSize);
                    var hasLoopback = loopbackBuffer.TryRead(loopSpan, out _);
                    var loopIn = hasLoopback
                        ? loopSpan
                        : emptyMemory.Span;

                    // Feed loopback data to APM
                    apm.AnalyzeReverseStream(loopIn);

                    // Inform APM about current analog microphone level (mapped from system scalar 0..1 to 0..255)
                    var currentLevel = Math.Clamp((int)MathF.Round(currentVolume * 255f), 0, 255);
                    apm.SetAnalogLevel(currentLevel);

                    // Process delayed microphone input
                    apm.ProcessStream(micIn, outSpan);

                    // After processing, get APM recommended analog level and apply back to system microphone volume
                    var recommendedLevel = apm.GetRecommendedAnalogLevel();
                    var recommendedVolume = Math.Clamp(Math.Clamp(recommendedLevel, 0, 255) / 255f, 0f, 1f);
                    if (Math.Abs(currentVolume - recommendedVolume) > 0.02f)
                        endpointVolume?.MasterVolumeLevelScalar = recommendedVolume;

                    // Log.LogDebug("APM.ProcessStream gains for mic and loopback: {GainMic} {GainLoop}", AudioExt.ApproximateGain(micIn), AudioExt.ApproximateGain(loopIn));

                    // Push processed output (fire-and-forget: drop if full)
                    outputBuffer.TryWrite(outSpan);
                    ArrayPools.SharedFloatPool.Return(micArray);
                    ArrayPools.SharedFloatPool.Return(outArray);
                    ArrayPools.SharedFloatPool.Return(loopArray);
                }
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex) {
                Log.LogError(ex, "Mic processing loop failed");
            }
            finally {
                ArrayPools.SharedFloatPool.Return(emptyArray);
            }
        }, processingCts.Token);
        // Start recording
        try {
            loopbackCapture?.StartRecording();
            graph.Start();
            inputNode.Start();
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to start NAudio recording");
            processingCts.CancelAndDisposeSilently();
            apm.DisposeSilently();
            graph.DisposeSilently();
            loopbackCapture.DisposeSilently();
            micDevice?.DisposeSilently();
            return null;
        }

        // Return async iterator
        var enumerateToken = cancellationToken;
        return Enumerate(enumerateToken);

        async IAsyncEnumerable<IMemoryOwner<float>> Enumerate([EnumeratorCancellation] CancellationToken ct)
        {
            try {
                const int frameLen = Constants.Audio.OpusFrameLength;
                while (!ct.IsCancellationRequested) {
                    var owner = ArrayPools.SharedFloatPool.LeaseArrayOwner(frameLen, true);
                    if (!outputBuffer.TryRead(owner.Span, out var whenReady)) {
                        owner.Dispose();
                        await whenReady.WaitAsync(ct).ConfigureAwait(false);
                        continue;
                    }
                    yield return owner;
                }
            }
            finally {
                // Stop and cleanup
                try {
                    inputNode?.Stop();
                    outputNode?.Stop();
                    graph?.Stop();
                    loopbackCapture?.StopRecording();
                }
                catch {
                    /* Ignore */
                }

                await processingCts.CancelAsync().ConfigureAwait(false);
                try { await processingTask.ConfigureAwait(false); }
                catch {
                    /* Ignore */
                }
                apm.DisposeSilently();
                inputNode?.DisposeSilently();
                outputNode?.DisposeSilently();
                graph?.DisposeSilently();
                micDevice?.DisposeSilently();
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
                    microphoneBuffer.TryWrite(inSpan);
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

            PushToBuffer(args, loopbackCapture.WaveFormat, loopbackBuffer);
        }

        void OnVolumeChanged(AudioVolumeNotificationData data)
        {
            currentVolume = Math.Clamp(data.MasterVolume, 0f, 1f);
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
                    ringBuffer.TryWrite(src);
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
            ringBuffer.TryWrite(tmp);
        }
        else {
            // Unsupported format: best effort - treat as bytes and skip
            // In practice, most devices provide 16-bit PCM or 32-bit float
        }
    }
}
