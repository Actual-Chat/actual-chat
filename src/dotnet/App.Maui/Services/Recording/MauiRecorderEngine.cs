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
    private static readonly string JSSetRecordingMethod = $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setRecording";
    private static readonly string JSSetConnectedMethod = $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setConnected";
    private static readonly string JSSetSignalDetectedMethod = $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setSignalDetected";
    private static readonly string JSSetVoiceActiveMethod = $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setVoiceActive";
    private static readonly string JSOnAudioPowerChangeMethod = $"{BlazorUIAppModule.ImportName}.RecorderStateHub.onAudioPowerChange";
    private static readonly string JSMicrophoneIsCapturedMethod = $"{BlazorUIAppModule.ImportName}.RecorderStateHub.microphoneIsCaptured";

    private readonly Lock _sync = new();
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

    public MauiRecorderEngine(UIHub hub)
    {
        _hub = hub;
        _noSignalDetectedDebouncer = Debouncer.New<Unit>(hub.Clocks.CoarseCpuClock, RecordingFiledInterval, _ => SetSignalDetected(false).AsTask());
    }

    [field: AllowNull, MaybeNull]
    private MicrophonePermissionHandler MicrophonePermissionHandler => field ??= _hub.Services.GetRequiredService<MicrophonePermissionHandler>();

    [field: AllowNull, MaybeNull]
    private IAudioCapture AudioCapture => field ??= _hub.Services.GetRequiredService<IAudioCapture>();

    [field: AllowNull, MaybeNull]
    private VoiceActivityDetector VoiceActivityDetector => field ??= _hub.Services.GetRequiredService<VoiceActivityDetector>();

    [field: AllowNull, MaybeNull]
    private IAudioRecorderBackend AudioRecorderBackend => field ??= _hub.Services.GetRequiredService<IAudioRecorderBackend>();

    [field: AllowNull, MaybeNull]
    private IAudioCodec AudioCodec => field ??= _hub.Services.GetRequiredService<IAudioCodec>();

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= _hub.LogFor<MauiRecorderEngine>();

    private IJSRuntime JS => _hub.JS;

    public async Task<bool> StartAsync(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        // Initialize and connect AudioStreamer
        _streamer ??= new AudioStreamer(_hub.HostInfo.BaseUrl);
        await _streamer.EnsureConnected(quickReconnect: true, cancellationToken).ConfigureAwait(false);
        await SetConnected(true).ConfigureAwait(false);

        // Ensure only one active recording CTS
        var newCts = cancellationToken.CreateLinkedTokenSource();
        var prevCts = Interlocked.Exchange(ref _recordingCts, newCts);
        if (prevCts != null) {
            try { await prevCts.CancelAsync(); } catch { /* ignore */ }
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

        // // Create a new stream; preSkip is unknown in MAUI path yet, set to 0
        // _currentStream = _streamer.CreateStream(sessionToken, preSkip: 0, chatId.ToString(), repliedChatEntryId?.ToString());
        // _currentStream.StartStreaming();
        return true;
    }

    public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        var recordingCts = Interlocked.Exchange(ref _recordingCts, null);
        if (recordingCts != null) {
            try { await recordingCts.CancelAsync(); } catch { /* ignore */ }
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

    private async Task ProcessMicrophoneStream(IAsyncEnumerable<IMemoryOwner<float>> frames, CancellationToken cancellationToken)
    {
        var vadRingBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10); // 10 seconds of VAD buffer
        var encodingRingBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10); // 10 seconds of encoding buffer
        var vadChannel = Channel.CreateUnbounded<VoiceActivityChange>(new UnboundedChannelOptions {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = true,
        });
        var sw = Stopwatch.StartNew();
        var minInterval = TimeSpan.FromMilliseconds(200);
        var lastMicCapturedAt = TimeSpan.Zero;
        var signaledMicCaptured = false;

        var detectSpeechTask = BackgroundTask.Run(async () => {
                await DetectSpeech(vadRingBuffer, vadChannel.Writer, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

        try {
            await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                using var _ = frame;
                var memory = frame.Memory;

                var isVadBufferPushed = vadRingBuffer.TryPush(memory.Span);
                var isEncodingBufferPushed = encodingRingBuffer.TryPush(memory.Span);
                if (!isVadBufferPushed || !isEncodingBufferPushed)
                    await Task.WhenAll(
                        vadRingBuffer.WhenPulled,
                        encodingRingBuffer.WhenPulled).ConfigureAwait(false);

                // Mark that we have a live microphone stream as soon as frames arrive
                if (!signaledMicCaptured) {
                    signaledMicCaptured = true;
                    await SetSignalDetected(true).ConfigureAwait(false);
                }

                var gain = AudioExt.ApproximateGain(memory.Span);
                // Log.LogInformation("Got frame {FrameLength} with gain={ApproximateGain}",
                //     memory.Length,
                //     gain);

                // Throttle JS interop call to once per 200 ms
                var now = sw.Elapsed;
                if (now - lastMicCapturedAt >= minInterval) {
                    lastMicCapturedAt = now;
                    await MicrophoneIsCaptured(gain).ConfigureAwait(false);
                }

                await RecordingHeartbeat(cancellationToken).ConfigureAwait(false);
            }
            await detectSpeechTask.ConfigureAwait(false);
        }
        finally {
            // Ensure UI/JS/Backend state is consistent when stream ends
            await SetVoiceActive(false).ConfigureAwait(false);
            await SetSignalDetected(false).ConfigureAwait(false);
            await SetRecording(false).ConfigureAwait(false);
        }
    }

    private async Task DetectSpeech(
        BlockRingBuffer<float> buffer,
        ChannelWriter<VoiceActivityChange> vadEvents,
        CancellationToken cancellationToken)
    {
        var vad = VoiceActivityDetector;
        await vad.EnsureInitialized(cancellationToken).ConfigureAwait(false);
        vad.Reset();

        while (!cancellationToken.IsCancellationRequested) {
            if (!buffer.TryPull(Constants.Audio.VadFrameLength, out var frame)) {
                await buffer.WhenPushed.ConfigureAwait(false);
                continue;
            }
            using var _ = frame;
            var vadResult = vad.AppendChunk(frame.Memory.Span);
            if (vadResult.HasEvent) {
                await vadEvents.WriteAsync(vadResult.Change.Value, cancellationToken);
                await SetVoiceActive(vadResult.Change.Value.Kind == VoiceActivityKind.Start).ConfigureAwait(false);
            }
            else if (vad.LastActivityEvent.Kind == VoiceActivityKind.Start)
                // Notify JS about audio power to keep heartbeat and UI animations
                await OnAudioPowerChange(vadResult.Gain).ConfigureAwait(false);
        }
    }

    private async Task Encode(
        BlockRingBuffer<float> buffer,
        ChannelWriter<IMemoryOwner<byte>> encodedFrames,
        CancellationToken cancellationToken)
    {
        try {
            async IAsyncEnumerable<IMemoryOwner<float>> ReadFrames()
            {
                while (!cancellationToken.IsCancellationRequested) {
                    if (!buffer.TryPull(Constants.Audio.OpusFrameLength, out var frame)) {
                        await buffer.WhenPushed.ConfigureAwait(false);
                        continue;
                    }
                    yield return frame; // ownership transferred to consumer
                }
            }

            await foreach (var packet in AudioCodec.Encode(ReadFrames(), cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false)) {
                await encodedFrames.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
            }
        }
        finally {
            encodedFrames.TryComplete();
        }
    }

    private async Task Send(ChannelReader<IMemoryOwner<byte>> encodedFrames, CancellationToken cancellationToken)
    {
        // Ensure streamer exists
        _streamer ??= new AudioStreamer(_hub.HostInfo.BaseUrl);

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
            return; // nothing to send without context

        await _streamer.EnsureConnected(quickReconnect: true, cancellationToken).ConfigureAwait(false);
        await SetConnected(_streamer.IsConnected).ConfigureAwait(false);

        // Create and start stream
        var stream = _streamer.CreateStream(sessionToken, preSkip: 0, chatId.ToString(), repliedChatEntryId);
        lock (_sync) {
            _currentStream = stream;
            // Clear replied id so it's only used once
            _repliedChatEntryId = null;
        }
        stream.StartStreaming();

        try {
            await foreach (var owner in encodedFrames.ReadAllAsync(cancellationToken).SuppressCancellation(cancellationToken).ConfigureAwait(false)) {
                using var _ = owner;
                var frame = owner.Memory.Span;
                if (frame.Length == 0) continue;
                stream.AddFrame(frame);
            }
        }
        catch (OperationCanceledException) {
            // ignore
        }
        finally {
            try { stream.Complete(); } catch { }
            try { await stream.DisposeAsync(); } catch { }
            lock (_sync) {
                if (ReferenceEquals(_currentStream, stream))
                    _currentStream = null;
            }
        }
    }

    private readonly struct PooledSliceOwner(IMemoryOwner<byte> rented, int length) : IMemoryOwner<byte>
    {
        public Memory<byte> Memory => rented.Memory[..length];
        public void Dispose() => rented.Dispose();
    }
}
