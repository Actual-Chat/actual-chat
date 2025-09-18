using System.Buffers;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services.Recording;

public class MauiRecorderEngine : IAudioRecorderEngine
{
    private static readonly TimeSpan RecordingFiledInterval = TimeSpan.FromMilliseconds(500);

    private static readonly string JSSetRecordingMethod =
        $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setRecording";

    private static readonly string JSSetConnectedMethod =
        $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setConnected";

    private static readonly string JSSetSignalDetectedMethod =
        $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setSignalDetected";

    private static readonly string JSSetVoiceActiveMethod =
        $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setVoiceActive";

    private static readonly string JSOnAudioPowerChangeMethod =
        $"{BlazorUIAppModule.ImportName}.RecorderStateHub.onAudioPowerChange";

    private static readonly string JSMicrophoneIsCapturedMethod =
        $"{BlazorUIAppModule.ImportName}.RecorderStateHub.microphoneIsCaptured";

    private readonly Lock _sync = new ();
    private readonly Debouncer<Unit> _noSignalDetectedDebouncer;

    private ChatId? _chatId;
    private string? _sessionToken;
    private ChatEntryId? _repliedChatEntryId;
    private AudioStreamer? _streamer;
    private AudioStreamer.AudioStream? _currentStream;
    private CancellationTokenSource? _recordingCts;
    private bool _isRecording;
    private bool _isConnected;
    private bool _isSignalDetected;
    private bool _isVoiceActive;
    private readonly UIHub _hub;

    [field: AllowNull] [field: MaybeNull]
    private MicrophonePermissionHandler MicrophonePermissionHandler
        => field ??= _hub.Services.GetRequiredService<MicrophonePermissionHandler>();

    [field: AllowNull] [field: MaybeNull]
    private IAudioCapture AudioCapture => field ??= _hub.Services.GetRequiredService<IAudioCapture>();

    [field: AllowNull] [field: MaybeNull]
    private VoiceActivityDetector VoiceActivityDetector
        => field ??= _hub.Services.GetRequiredService<VoiceActivityDetector>();

    [field: AllowNull] [field: MaybeNull]
    private IAudioRecorderBackend AudioRecorderBackend
        => field ??= _hub.Services.GetRequiredService<IAudioRecorderBackend>();

    [field: AllowNull] [field: MaybeNull]
    private IAudioCodec AudioCodec => field ??= _hub.Services.GetRequiredService<IAudioCodec>();

    [field: AllowNull] [field: MaybeNull]
    private ILogger Log => field ??= _hub.LogFor<MauiRecorderEngine>();

    private IJSRuntime JS => _hub.JS;

    public MauiRecorderEngine(UIHub hub)
    {
        _hub = hub;
        _noSignalDetectedDebouncer = Debouncer.New<Unit>(hub.Clocks.CoarseCpuClock,
            RecordingFiledInterval,
            _ => SetSignalDetected(false).AsTask());
    }

    public async Task<bool> StartAsync(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        // Initialize and connect AudioStreamer
        _streamer ??= new AudioStreamer(_hub.HostInfo.BaseUrl);
        await _streamer.EnsureConnected(true, cancellationToken).ConfigureAwait(false);
        await SetConnected(true).ConfigureAwait(false);

        // Ensure only one active recording CTS
        var newCts = cancellationToken.CreateLinkedTokenSource();
        var prevCts = Interlocked.Exchange(ref _recordingCts, newCts);
        if (prevCts != null) {
            try { await prevCts.CancelAsync(); }
            catch {
                /* ignore */
            }
            prevCts.Dispose();
        }
        var token = newCts.Token;

        var microphoneStream = await AudioCapture.Capture(token).ConfigureAwait(false);
        if (microphoneStream is null) {
            Log.LogWarning("Microphone stream is unavailable");
            await SetRecording(false).ConfigureAwait(false);
            return false;
        }

        // Start recording
        lock (_sync) {
            _chatId = chatId;
            _sessionToken = sessionToken;
            _repliedChatEntryId = repliedChatEntryId;
        }
        await SetRecording(true).ConfigureAwait(false);
        await SetSignalDetected(false).ConfigureAwait(false);
        await SetVoiceActive(false).ConfigureAwait(false);

        _ = BackgroundTask.Run(async () => {
                await ProcessMicrophoneStream(microphoneStream, token).ConfigureAwait(false);
            },
            token);

        return true;
    }

    public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        var recordingCts = Interlocked.Exchange(ref _recordingCts, null);
        if (recordingCts != null) {
            try { await recordingCts.CancelAsync(); }
            catch {
                /* ignore */
            }
            recordingCts.Dispose();
        }

        AudioStreamer.AudioStream? stream;
        lock (_sync) {
            stream = _currentStream;
            _currentStream = null;
            _chatId = null;
        }

        if (stream == null) {
            await SetRecording(false).ConfigureAwait(false);
            await SetVoiceActive(false).ConfigureAwait(false);
            await SetSignalDetected(false).ConfigureAwait(false);
            return true;
        }

        stream.Complete();
        await stream.DisposeAsync();
        await SetRecording(false).ConfigureAwait(false);
        await SetVoiceActive(false).ConfigureAwait(false);
        await SetSignalDetected(false).ConfigureAwait(false);
        _noSignalDetectedDebouncer.Reset();
        return true;
    }

    public async ValueTask EnsureConnected(bool quickReconnect, CancellationToken cancellationToken)
    {
        _streamer ??= new AudioStreamer(_hub.HostInfo.BaseUrl);
        await _streamer.EnsureConnected(quickReconnect, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask ConversationSignal(CancellationToken cancellationToken)
    {
        VoiceActivityDetector.ConversationSignal();
        return ValueTask.CompletedTask;
    }

    public async Task<AudioRecorder.AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
    {
        var permissionStatus = await MicrophonePermissionHandler.Check(cancellationToken);
        var lastVadEvent = VoiceActivityDetector.LastActivityEvent;

        bool isSignalDetected, isConnected;
        lock (_sync) {
            isSignalDetected = _isSignalDetected;
            isConnected = _currentStream != null || (_streamer?.IsConnected ?? false);
        }

        return new AudioRecorder.AudioDiagnosticsState {
            HasMicrophonePermission = permissionStatus,
            IsConnected = isConnected,
            HasMicrophoneStream = isSignalDetected,
            LastVadEvent = new AudioRecorder.VadEvent {
                Kind = lastVadEvent.Kind.ToString(),
                Offset = lastVadEvent.OffsetSeconds,
                Duration = lastVadEvent.DurationSeconds ?? 0,
                SpeechProb = lastVadEvent.SpeechProb,
            },
            IsSignalDetected = isSignalDetected,
            IsVadActive = VoiceActivityDetector.IsInitialized,
        };
    }

    // Private methods

    private ValueTask SetRecording(bool isRecording)
    {
        var previous = Interlocked.Exchange(ref _isRecording, isRecording);
        if (previous == isRecording)
            return ValueTask.CompletedTask;

        StateHasChanged();
        return JS.InvokeVoidAsync(JSSetRecordingMethod, isRecording);
    }

    private ValueTask SetConnected(bool isConnected)
    {
        var previous = Interlocked.Exchange(ref _isConnected, isConnected);
        if (previous == isConnected)
            return ValueTask.CompletedTask;

        StateHasChanged();
        return JS.InvokeVoidAsync(JSSetConnectedMethod, isConnected);
    }

    private ValueTask SetSignalDetected(bool isSignalDetected)
    {
        if (isSignalDetected)
            _noSignalDetectedDebouncer.Debounce(Unit.Default); // Schedule a debounced no signal detected event

        var previous = Interlocked.Exchange(ref _isSignalDetected, isSignalDetected);
        if (previous == isSignalDetected)
            return ValueTask.CompletedTask;

        StateHasChanged();
        return JS.InvokeVoidAsync(JSSetSignalDetectedMethod, isSignalDetected);
    }

    private ValueTask SetVoiceActive(bool isVoiceActive)
    {
        var previous = Interlocked.Exchange(ref _isVoiceActive, isVoiceActive);
        if (previous == isVoiceActive)
            return ValueTask.CompletedTask;

        StateHasChanged();
        return JS.InvokeVoidAsync(JSSetVoiceActiveMethod, isVoiceActive);
    }

    private ValueTask OnAudioPowerChange(double power)
        => JS.InvokeVoidAsync(JSOnAudioPowerChangeMethod, power);

    private async ValueTask MicrophoneIsCaptured(double gain)
    {
        await SetSignalDetected(true).ConfigureAwait(false);
        await JS.InvokeVoidAsync(JSMicrophoneIsCapturedMethod, gain).ConfigureAwait(false);
    }

    private void StateHasChanged()
    {
        bool isRecording, isSignalDetected, isConnected, isVoiceActive;
        lock (_sync) {
            isRecording = _isRecording;
            isSignalDetected = _isSignalDetected;
            isConnected = _isConnected;
            isVoiceActive = _isVoiceActive;
        }
        AudioRecorderBackend.OnRecordingStateChange(isRecording, isSignalDetected, isConnected, isVoiceActive);
    }

    private async Task RecordingHeartbeat(CancellationToken cancellationToken)
    {
        try {
            ChatId? chatId;
            bool isRecording;
            lock (_sync) {
                chatId = _chatId;
                isRecording = _isRecording;
            }
            if (!isRecording)
                return;

            if (chatId is null) {
                await StopAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var backendRecording = AudioRecorderBackend.IsRecording(chatId.Value);
            if (backendRecording)
                return;

            await StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to run recording heartbeat");
            await StopAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ProcessMicrophoneStream(
        IAsyncEnumerable<IMemoryOwner<float>> frames,
        CancellationToken cancellationToken)
    {
        // Buffers: keep small encoding buffer for ~2s preroll; VAD buffer to form VAD-sized chunks
        var vadRingBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10); // ~10s
        var encodingRingBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 2); // ~2s

        var sw = Stopwatch.StartNew();
        var minInterval = TimeSpan.FromMilliseconds(200);
        var lastMicCapturedAt = TimeSpan.Zero;
        var processToken = cancellationToken;

        // VAD state and combined encode+send worker
        var vad = VoiceActivityDetector;
        await vad.EnsureInitialized(processToken).ConfigureAwait(false);
        vad.Reset();
        bool voiceActive = false;

        CancellationTokenSource? encodeSendCts = null;
        Task? encodeSendTask = null;

        try {
            await foreach (var frame in frames.WithCancellation(processToken).ConfigureAwait(false)) {
                using var _ = frame;
                var memory = frame.Memory;

                // Push current microphone frame into both buffers
                var isVadPushed = vadRingBuffer.TryPush(memory.Span);

                // Maintain encoding buffer with small rolling preroll
                bool isEncodingPushed;
                if (voiceActive)
                    isEncodingPushed = encodingRingBuffer.TryPush(memory.Span);
                else {
                    isEncodingPushed = encodingRingBuffer.TryPush(memory.Span);
                    if (!isEncodingPushed) {
                        // Trim oldest audio in Opus-sized chunks to keep constant size preroll
                        var retryCount = 0;
                        while (!isEncodingPushed && retryCount++ < 32) {
                            if (encodingRingBuffer.TryPull(Constants.Audio.OpusFrameLength, out var dropped))
                                using (dropped) { /* drop */ }
                            else
                                break;
                            isEncodingPushed = encodingRingBuffer.TryPush(memory.Span);
                        }
                    }
                }

                if (!isVadPushed || (voiceActive && !isEncodingPushed))
                    await Task.WhenAll(vadRingBuffer.WhenPulled, encodingRingBuffer.WhenPulled).ConfigureAwait(false);

                await SetSignalDetected(true).ConfigureAwait(false);

                // Throttle JS interop for microphone gain
                var now = sw.Elapsed;
                if (now - lastMicCapturedAt >= minInterval) {
                    lastMicCapturedAt = now;
                    var gain = AudioExt.ApproximateGain(memory.Span);
                    await MicrophoneIsCaptured(gain).ConfigureAwait(false);
                }

                // Consume VAD-sized chunks and react to events inline
                while (vadRingBuffer.TryPull(Constants.Audio.VadFrameLength, out var vadFrame)) {
                    using var __ = vadFrame;
                    var vadResult = vad.AppendChunk(vadFrame.Memory.Span);

                    if (vadResult.HasEvent) {
                        if (vadResult.Change.Value.Kind == VoiceActivityKind.Start) {
                            if (voiceActive)
                                continue;

                            // Start combined encode+send worker + stream
                            var stream = await StartStreamingIfPossible(processToken).ConfigureAwait(false);
                            if (stream == null)
                                continue;

                            var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            var localTask = BackgroundTask.Run(
                                async () => await EncodeAndSend(encodingRingBuffer, stream, localCts.Token).ConfigureAwait(false),
                                localCts.Token);
                            encodeSendCts = localCts;
                            encodeSendTask = localTask;
                            voiceActive = true;
                            await SetVoiceActive(true).ConfigureAwait(false);
                        } else {
                            // VAD End: stop worker if running
                            if (!voiceActive)
                                continue;

                            voiceActive = false;
                            await SetVoiceActive(false).ConfigureAwait(false);

                            // Capture and null the worker references sequentially; Interlocked is unnecessary here
                            var localCts = encodeSendCts;
                            encodeSendCts = null;
                            if (localCts != null) {
                                try { await localCts.CancelAsync(); } catch { /* ignore */ }
                                localCts.Dispose();
                            }

                            var localTask = encodeSendTask;
                            encodeSendTask = null;
                            if (localTask != null)
                                try { await localTask.ConfigureAwait(false); } catch (Exception e) {
                                    Log.LogError(e, "Failed to stop encode/send worker");
                                }
                        }
                    } else if (vad.LastActivityEvent.Kind == VoiceActivityKind.Start)
                        // Maintain UI/JS heartbeat/animations
                        await OnAudioPowerChange(vadResult.Gain).ConfigureAwait(false);
                }

                await RecordingHeartbeat(processToken).ConfigureAwait(false);
            }
        }
        finally {
            // Ensure worker and stream are stopped
            try {
                if (encodeSendCts != null) {
                    try { await encodeSendCts.CancelAsync(); } catch { /* ignore */ }
                    encodeSendCts.Dispose();
                }
                if (encodeSendTask != null)
                    try { await encodeSendTask.ConfigureAwait(false); } catch { /* ignore */ }
            }
            catch { /* ignore */ }

            await SetVoiceActive(false).ConfigureAwait(false);
            await SetSignalDetected(false).ConfigureAwait(false);
            await SetRecording(false).ConfigureAwait(false);
        }
        return;

        async Task<AudioStreamer.AudioStream?> StartStreamingIfPossible(CancellationToken token)
        {
            // Capture context
            string? sessionToken;
            ChatId? chatId;
            string? repliedChatEntryId;
            lock (_sync) {
                sessionToken = _sessionToken;
                chatId = _chatId;
                repliedChatEntryId = _repliedChatEntryId?.ToString();
            }
            if (sessionToken is null || chatId is null)
                return null;

            _streamer ??= new AudioStreamer(_hub.HostInfo.BaseUrl);
            await _streamer.EnsureConnected(true, token).ConfigureAwait(false);
            await SetConnected(_streamer.IsConnected).ConfigureAwait(false);

            var stream = _streamer.CreateStream(sessionToken, 0, chatId.ToString(), repliedChatEntryId);
            lock (_sync) {
                _currentStream = stream;
                // Clear replied id so it's only used once
                _repliedChatEntryId = null;
            }
            stream.StartStreaming();
            return stream;
        }

        async Task EncodeAndSend(BlockRingBuffer<float> buffer, AudioStreamer.AudioStream stream, CancellationToken token)
        {
            try {
                await foreach (var packet in AudioCodec.Encode(ReadFrames(), token).ConfigureAwait(false)) {
                    using var _ = packet;
                    var frame = packet.Memory.Span;
                    if (frame.Length == 0)
                        continue;
                    stream.AddFrame(frame);
                }
            }
            catch (OperationCanceledException) {
                // ignore
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to encode/send audio");
                throw;
            }
            finally {
                try { stream.Complete(); } catch { /* ignore */ }
                try { await stream.DisposeAsync(); } catch { /* ignore */ }
                lock (_sync)
                    if (ReferenceEquals(_currentStream, stream))
                        _currentStream = null;
            }
            return;

            async IAsyncEnumerable<IMemoryOwner<float>> ReadFrames()
            {
                while (!token.IsCancellationRequested) {
                    if (!buffer.TryPull(Constants.Audio.OpusFrameLength, out var frame)) {
                        await buffer.WhenPushed.ConfigureAwait(false);
                        continue;
                    }
                    yield return frame; // ownership transferred to encoder
                }
            }
        }
    }
}
