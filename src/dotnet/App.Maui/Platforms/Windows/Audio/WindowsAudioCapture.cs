using System.Buffers;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.Audio.APM;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using Role = NAudio.CoreAudioApi.Role;

namespace ActualChat.App.Maui.Audio;

public sealed class WindowsAudioCapture(ILogger<WindowsAudioCapture> log) : IAudioCapture
{
    private const int CaptureBufferMs = Constants.Audio.ApmFrameDurationMs;
    private const int MicDelayMs = 10;
    private const int MicDelaySamples = Constants.Audio.RecordingSampleRate * MicDelayMs / 1000;
    private static readonly TimeSpan GraphRetryDelay = TimeSpan.FromMilliseconds(250);
    private ILogger Log { get; } = log;

    public async Task<AudioCaptureResult> Capture(CancellationToken cancellationToken)
    {
        // Set by TryCreateGraph: WinRT already names the failure, and it's the only place
        // AccessDenied can be told apart from a device that simply isn't there.
        var failure = RecorderStartOutcome.Started;
        var apmTap = WebRtcApmTap.TryStart(Log);

        // Constructing it loads webrtc-apm, so a missing or wrong-architecture native throws here
        AudioProcessingModule? apm = null;
        try {
            apm = new AudioProcessingModule(
                new StreamConfig(Constants.Audio.RecordingSampleRate, Constants.Audio.Channels),
                new StreamConfig(Constants.Audio.RecordingSampleRate, Constants.Audio.Channels));
            apm.Configure(cfg => cfg
                .EnableEchoCanceller(true)
                .EnableNoiseSuppression(true, NoiseSuppressionLevel.Moderate)
                .EnableAutomaticGainControl(true)
                .EnableHighPassFilter(true)
                .SetPipeline(false, false));
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to set up AudioProcessingModule; proceeding without APM features");
        }

        var micEncoding = AudioEncodingProperties.CreatePcm(
            Constants.Audio.RecordingSampleRate,
            Constants.Audio.Channels,
            32);
        micEncoding.Subtype = MediaEncodingSubtypes.Float;
        var settings = new AudioGraphSettings(AudioRenderCategory.Other) {
            // Use a non-communications category to avoid OS voice processing (AEC/NS/AGC) on capture
            QuantumSizeSelectionMode = QuantumSizeSelectionMode.ClosestToDesired,
            DesiredSamplesPerQuantum = Constants.Audio.RecordingSampleRate / 1000 * CaptureBufferMs,
        };

        var outputBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);
        var microphoneBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);
        var loopbackBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10);

        const int apmFrameSize = Constants.Audio.RecordingSampleRate
            / 1000
            * Constants.Audio.ApmFrameDurationMs
            * Constants.Audio.Channels;

        // Everything below is acquired inside the try and released by Stop(), so a failure at any
        // step - or a caller that never enumerates the result - can't leave a running graph behind
        AudioGraph? graph = null;
        AudioDeviceInputNode? inputNode = null;
        AudioFrameOutputNode? outputNode = null;
        WasapiCapture? loopbackCapture = null;
        MMDeviceEnumerator? deviceEnumerator = null;
        DefaultCaptureDeviceWatcher? deviceWatcher = null;
        MMDevice? micDevice = null;
        AudioEndpointVolume? endpointVolume = null;
        CancellationTokenSource? processingCts = null;
        Task? processingTask = null;
        var whenStopped = TaskCompletionSourceExt.New();
        var isStopping = 0;
        var currentVolume = 0.5f;
        var initialVolume = 0.5f;
        var lastAppliedVolume = float.NaN;

        try {
            // A wedged WinRT audio session sometimes clears on its own, and one retry costs a
            // quarter of a second against a failure the user can otherwise only fix by restarting
            var isGraphCreated = await TryCreateGraph().ConfigureAwait(true);
            if (!isGraphCreated) {
                await Task.Delay(GraphRetryDelay, cancellationToken).ConfigureAwait(true);
                isGraphCreated = await TryCreateGraph().ConfigureAwait(true);
            }
            if (!isGraphCreated) {
                await Stop().ConfigureAwait(false);
                return AudioCaptureResult.Failed(failure.Result, failure.Code);
            }

            try {
                deviceEnumerator = new MMDeviceEnumerator();
                // Multimedia, matching what CreateDeviceInputNodeAsync opens below. Under
                // Communications, AGC drove a mic nobody was recording and never converged.
                micDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                var loopbackDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                loopbackCapture = new CustomWasapiLoopbackCapture(loopbackDevice, true, CaptureBufferMs);
                loopbackCapture.DataAvailable += OnLoopbackCaptureOnDataAvailable;
                // NAudio's only channel for a capture-thread failure. Unsubscribed, losing the
                // render endpoint silently ends the AEC reference for the rest of the session.
                loopbackCapture.RecordingStopped += OnLoopbackRecordingStopped;
                loopbackCapture.WaveFormat =
                    WaveFormat.CreateIeeeFloatWaveFormat(Constants.Audio.RecordingSampleRate, 1);
            }
            catch (Exception e) {
                // Under NativeAOT, ILC emits an invalid body for NAudio's COM ComObject wrapper
                // ctor (InvalidProgramException on MMDeviceEnumeratorComObject..ctor()).
                // TrimmerRootAssembly on NAudio.Wasapi does not fix it. Same pattern as the
                // WindowConfigurator / WindowsAppIconBadge known issues: wrap and degrade.
                // The mic itself goes through WinRT AudioGraph above (AOT-safe), so we continue
                // without the AEC loopback reverse stream and without mic volume control.
                Log.LogWarning(e,
                    "NAudio capture devices unavailable; continuing without AEC loopback and mic volume control");
                loopbackCapture.DisposeSilently();
                loopbackCapture = null;
                micDevice.DisposeSilently();
                micDevice = null;
                deviceEnumerator.DisposeSilently();
                deviceEnumerator = null;
            }

            if (deviceEnumerator != null) {
                try {
                    // The endpoint the graph actually opened, so a re-pick landing back on it is
                    // ignored: WinRT ids embed the WASAPI endpoint id the notification carries.
                    var openedDeviceId = inputNode?.Device?.Id;
                    deviceWatcher = new DefaultCaptureDeviceWatcher(openedDeviceId, OnDefaultCaptureDeviceChanged);
                    deviceEnumerator.RegisterEndpointNotificationCallback(deviceWatcher);
                }
                catch (Exception e) {
                    // Registering a managed COM callback is the most AOT-fragile call here, and
                    // losing it only costs the device-change handling
                    Log.LogWarning(e, "Can't watch for default capture device changes");
                    deviceWatcher = null;
                }
            }

            // Throttled access to microphone volume to avoid per-frame COM calls
            endpointVolume = micDevice?.AudioEndpointVolume;
            currentVolume = Math.Clamp(endpointVolume?.MasterVolumeLevelScalar ?? 0.5f, 0f, 1f);
            initialVolume = currentVolume;
            endpointVolume?.OnVolumeNotification += OnVolumeChanged;

            processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var processingToken = processingCts.Token;
            processingTask = BackgroundTask.Run(async () => {
                var emptyArray = ArrayPools.SharedFloatPool.Rent(apmFrameSize);
                var emptyMemory = emptyArray.AsMemory(0, apmFrameSize);
                emptyMemory.Span.Clear();
                var isSilent = true;
                var windowPeak = 0f;
                var lastSilenceCheckStamp = Stopwatch.GetTimestamp();
                try {
                    while (!processingToken.IsCancellationRequested) {
                        // Enough samples must be buffered to enforce MicDelaySamples
                        if (microphoneBuffer.Count < apmFrameSize + MicDelaySamples) {
                            var whenReady = microphoneBuffer.WhenReadyToRead();
                            if (whenReady != null)
                                await whenReady.WaitAsync(processingToken).ConfigureAwait(false);
                            else
                                await Task.Delay(1, processingToken).ConfigureAwait(false);
                            continue;
                        }

                        var micArray = ArrayPools.SharedFloatPool.Rent(apmFrameSize);
                        var micIn = micArray.AsSpan(0, apmFrameSize);
                        if (!microphoneBuffer.TryRead(micIn, out var micWhenReady)) {
                            ArrayPools.SharedFloatPool.Return(micArray);
                            await micWhenReady.WaitAsync(processingToken).ConfigureAwait(false);
                            continue;
                        }

                        // A withheld mic still delivers frames, just all-zero ones. Checked pre-APM,
                        // where no OS voice processing can have gated the samples to zero.
                        if (isSilent) {
                            foreach (var sample in micIn) {
                                var level = MathF.Abs(sample);
                                if (level > windowPeak)
                                    windowPeak = level;
                            }
                            var silenceCheckStamp = Stopwatch.GetTimestamp();
                            var sinceLastCheck = Stopwatch.GetElapsedTime(lastSilenceCheckStamp, silenceCheckStamp);
                            if (sinceLastCheck.TotalSeconds >= 1.0) {
                                lastSilenceCheckStamp = silenceCheckStamp;
                                if (windowPeak > 0f) {
                                    isSilent = false;
                                    Log.LogInformation("Microphone capture: first audio, peak {Peak:F3}", windowPeak);
                                }
                                else
                                    Log.LogWarning("Microphone capture: nothing but digital silence so far");
                                windowPeak = 0f;
                            }
                        }

                        var outArray = ArrayPools.SharedFloatPool.Rent(apmFrameSize);
                        var outSpan = outArray.AsSpan(0, apmFrameSize);

                        // Zeros when the loopback capture is unavailable - the APM still needs a reverse stream
                        var loopArray = ArrayPools.SharedFloatPool.Rent(apmFrameSize);
                        var loopSpan = loopArray.AsSpan(0, apmFrameSize);
                        var hasLoopback = loopbackBuffer.TryRead(loopSpan, out _);
                        var loopIn = hasLoopback
                            ? loopSpan
                            : emptyMemory.Span;

                        if (apm == null)
                            micIn.CopyTo(outSpan); // No APM: raw capture beats no capture
                        else {
                            apm.AnalyzeReverseStream(loopIn);

                            // The APM clears the stream delay after every ProcessStream, so it has to
                            // be re-stated per frame. It's the buffering delay between the reverse
                            // frame and the capture frame carrying its echo - i.e. the mic hold-back.
                            apm.SetDelay(MicDelayMs);

                            // The APM's analog level is 0..255, the system scalar is 0..1
                            var currentLevel = Math.Clamp((int)MathF.Round(currentVolume * 255f), 0, 255);
                            apm.SetAnalogLevel(currentLevel);
                            apm.ProcessStream(micIn, outSpan);

                            var recommendedLevel = apm.GetRecommendedAnalogLevel();
                            var recommendedVolume = Math.Clamp(Math.Clamp(recommendedLevel, 0, 255) / 255f, 0f, 1f);
                            if (Math.Abs(currentVolume - recommendedVolume) > 0.02f) {
                                endpointVolume?.MasterVolumeLevelScalar = recommendedVolume;
                                // OnVolumeChanged lands asynchronously, so without this the same write
                                // repeats on every 10 ms frame until the notification catches up
                                currentVolume = recommendedVolume;
                                lastAppliedVolume = recommendedVolume;
                            }
                        }

                        apmTap?.Add(micIn, loopIn, outSpan, hasLoopback);

                        // Fire-and-forget: drop if full
                        outputBuffer.TryWrite(outSpan);
                        ArrayPools.SharedFloatPool.Return(micArray);
                        ArrayPools.SharedFloatPool.Return(outArray);
                        ArrayPools.SharedFloatPool.Return(loopArray);
                    }
                }
                catch (OperationCanceledException) {
                    /* Expected */
                }
                catch (Exception ex) {
                    // Ending is what lets it restart: dying quietly leaves the graph and loopback
                    // running with the heartbeat stopped, so the mic stays hot and nothing records.
                    Log.LogError(ex, "Mic processing loop failed - ending the capture");
                    _ = Stop();
                }
                finally {
                    ArrayPools.SharedFloatPool.Return(emptyArray);
                }
            }, processingCts.Token);

            if (loopbackCapture is not null)
                try {
                    loopbackCapture.StartRecording();
                }
                catch (Exception e) {
                    // Degraded, not fatal: WASAPI Initialize happens inside StartRecording, so an
                    // app holding the render endpoint exclusively used to kill the whole recording.
                    Log.LogWarning(e, "AEC loopback failed to start; continuing without it");
                    loopbackCapture.DisposeSilently();
                    loopbackCapture = null;
                }

            try {
                graph!.Start();
                inputNode!.Start();
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to start audio capture");
                await Stop().ConfigureAwait(false);
                return AudioCaptureResult.Failed(RecorderStartResult.Unknown, e.GetType().Name);
            }
        }
        catch (Exception e) {
            await Stop().ConfigureAwait(false);
            if (e is not OperationCanceledException)
                Log.LogError(e, "Failed to set up audio capture");

            throw;
        }

        // Stop() must stay reachable without the enumerator: the caller can fail between here and
        // the first MoveNextAsync, and an abandoned running graph wedges WinRT audio for this
        // process - every later AudioGraph.CreateAsync then fails until the app is restarted.
        var stopRegistration = cancellationToken.Register(() => _ = Stop());
        return AudioCaptureResult.Ok(Enumerate(cancellationToken));

        async Task<bool> TryCreateGraph() {
            graph.DisposeSilently();
            graph = null;
            inputNode = null;
            outputNode = null;

            var graphCreate = await AudioGraph.CreateAsync(settings).AsTask(cancellationToken).ConfigureAwait(true);
            if (graphCreate.Status != AudioGraphCreationStatus.Success || graphCreate.Graph is null) {
                // ExtendedError carries the HRESULT - without it "UnknownFailure" is all we ever see
                Log.LogWarning(graphCreate.ExtendedError,
                    "AudioGraph creation failed: {Status}",
                    graphCreate.Status);
                failure = new RecorderStartOutcome(
                    graphCreate.Status == AudioGraphCreationStatus.DeviceNotAvailable
                        ? RecorderStartResult.NoDevice
                        : RecorderStartResult.Unknown,
                    $"AudioGraph.{graphCreate.Status}");
                return false;
            }

            graph = graphCreate.Graph;
            var inputCreate = await graph
                .CreateDeviceInputNodeAsync(MediaCategory.Other, micEncoding)
                .AsTask(cancellationToken)
                .ConfigureAwait(true);
            if (inputCreate.Status != AudioDeviceNodeCreationStatus.Success || inputCreate.DeviceInputNode is null) {
                Log.LogWarning(inputCreate.ExtendedError,
                    "Microphone input node creation failed: {Status} - is the mic available?",
                    inputCreate.Status);
                failure = new RecorderStartOutcome(
                    inputCreate.Status switch {
                        AudioDeviceNodeCreationStatus.AccessDenied => RecorderStartResult.NoPermission,
                        AudioDeviceNodeCreationStatus.DeviceNotAvailable => RecorderStartResult.NoDevice,
                        _ => RecorderStartResult.Unknown,
                    },
                    $"AudioInputNode.{inputCreate.Status}");
                return false;
            }

            inputNode = inputCreate.DeviceInputNode;
            Log.LogInformation(
                "Microphone capture ready: '{DeviceName}', {SamplesPerQuantum} samples/quantum at {SampleRate}Hz",
                inputNode.Device?.Name,
                graph.SamplesPerQuantum,
                graph.EncodingProperties.SampleRate);

            // Ensure no built-in audio effects are applied on the capture path
            inputNode.EffectDefinitions.Clear();

            outputNode = graph.CreateFrameOutputNode(micEncoding);
            inputNode.AddOutgoingConnection(outputNode);
            graph.QuantumStarted += QuantumEventHandler;
            graph.UnrecoverableErrorOccurred += OnUnrecoverableError;
            return true;
        }

        async IAsyncEnumerable<IMemoryOwner<float>> Enumerate([EnumeratorCancellation] CancellationToken ct) {
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
                stopRegistration.Dispose();
                await Stop().ConfigureAwait(false);
            }
        }

        Task Stop() {
            if (Interlocked.Exchange(ref isStopping, 1) == 0)
                _ = StopInternal();

            return whenStopped.Task;
        }

        async Task StopInternal() {
            try {
                try {
                    inputNode?.Stop();
                    outputNode?.Stop();
                    graph?.Stop();
                    loopbackCapture?.StopRecording();
                }
                catch {
                    /* Ignore */
                }

                UnwatchDefaultCaptureDevice();

                // The processing loop touches apm and endpointVolume, so it has to be drained
                // before either of them is released
                if (processingCts != null)
                    await processingCts.CancelAsync().ConfigureAwait(false);
                if (processingTask != null) {
                    try {
                        await processingTask.ConfigureAwait(false);
                    }
                    catch {
                        /* Ignore */
                    }
                }
                apmTap?.Stop();

                RestoreMicrophoneVolume();
                apm.DisposeSilently();
                inputNode?.DisposeSilently();
                outputNode?.DisposeSilently();
                graph?.DisposeSilently();
                micDevice?.DisposeSilently();
                deviceEnumerator?.DisposeSilently();
                loopbackCapture?.Dispose();
                processingCts.DisposeSilently();
                outputBuffer.Dispose();
                microphoneBuffer.Dispose();
                loopbackBuffer.Dispose();
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to release the audio capture pipeline");
            }
            finally {
                whenStopped.TrySetResult();
            }
        }

        void UnwatchDefaultCaptureDevice() {
            if (deviceEnumerator == null || deviceWatcher == null)
                return;

            try {
                deviceEnumerator.UnregisterEndpointNotificationCallback(deviceWatcher);
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to stop watching default capture device changes");
            }
            deviceWatcher = null;
        }

        void RestoreMicrophoneVolume() {
            if (endpointVolume == null)
                return;

            try {
                endpointVolume.OnVolumeNotification -= OnVolumeChanged;
                // Only undo what the APM's AGC did: a manual change made while recording wins.
                // Without this the app permanently moves the user's Windows microphone level.
                if (!float.IsNaN(lastAppliedVolume)
                    && Math.Abs(endpointVolume.MasterVolumeLevelScalar - lastAppliedVolume) < 0.01f)
                    endpointVolume.MasterVolumeLevelScalar = initialVolume;
            }
            catch (Exception e) {
                Log.LogWarning(e, "Failed to restore the microphone volume");
            }
            // Its native callback registration otherwise lives until finalization.
            endpointVolume.DisposeSilently();
            endpointVolume = null;
        }

        void OnUnrecoverableError(AudioGraph sender, AudioGraphUnrecoverableErrorOccurredEventArgs args) {
            // WinRT's own "this graph is dead" signal. Leaving it running is what wedges audio for
            // the rest of the process, so end the capture and let the next attempt build a new one.
            Log.LogWarning("AudioGraph hit an unrecoverable error: {Error}", args.Error);
            _ = Stop();
        }

        void OnLoopbackRecordingStopped(object? sender, StoppedEventArgs e) {
            if (e.Exception is { } error)
                Log.LogWarning(error, "AEC loopback capture stopped - echo cancellation is degraded");
        }

        void OnDefaultCaptureDeviceChanged() {
            // The graph is bound to the endpoint it opened, so it goes on delivering silence from a
            // device that is no longer the user's. Ending the capture is honest; recording resumes
            // on the new device the next time it's started.
            Log.LogWarning("Default capture device changed - ending the capture");
            _ = Stop();
        }

        void QuantumEventHandler(AudioGraph sender, object args) {
            // Guard flag: Stop() may already be disposing outputNode on another thread
            if (Volatile.Read(ref isStopping) != 0 || cancellationToken.IsCancellationRequested)
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
                    WindowsRuntimeMarshal.TryGetDataUnsafe(reference, out var dataPtr, out var capacity);
                    if (dataPtr == IntPtr.Zero || capacity == 0)
                        return;

                    var floatCount = (int)capacity / sizeof(float);
                    var inSpan = new ReadOnlySpan<float>((void*)dataPtr, floatCount);
                    microphoneBuffer.TryWrite(inSpan);
                }
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to process audio frame");
            }
        }

        void OnLoopbackCaptureOnDataAvailable(object? _, WaveInEventArgs args) {
            // Guard flag: Stop() may already be disposing loopbackCapture on another thread
            if (Volatile.Read(ref isStopping) != 0 || cancellationToken.IsCancellationRequested)
                return;
            if (args.BytesRecorded == 0)
                return;

            PushToBuffer(args, loopbackCapture!.WaveFormat, loopbackBuffer);
        }

        void OnVolumeChanged(AudioVolumeNotificationData data)
            => currentVolume = Math.Clamp(data.MasterVolume, 0f, 1f);
    }

    // Private methods

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
                var s = (short)(buffer[i] | (buffer[i + 1] << 8));
                tmp[o] = s / 32768f;
            }
            ringBuffer.TryWrite(tmp);
        }
        // Any other format is dropped on purpose: in practice devices deliver one of the two above.
    }

    // Nested types

    private sealed class DefaultCaptureDeviceWatcher(
        string? openedDeviceId,
        Action onChanged
        ) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow dataFlow, Role role, string defaultDeviceId)
        {
            if (dataFlow != DataFlow.Capture || role != Role.Multimedia)
                return;
            // Still the endpoint the graph holds
            if (openedDeviceId != null
                && !defaultDeviceId.IsNullOrEmpty()
                && openedDeviceId.Contains(defaultDeviceId, StringComparison.OrdinalIgnoreCase))
                return;

            onChanged();
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string deviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string deviceId, PropertyKey propertyKey) { }
    }
}
