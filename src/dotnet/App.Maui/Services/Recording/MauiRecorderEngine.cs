using System.Buffers;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;

namespace ActualChat.App.Maui.Services.Recording;

public class MauiRecorderEngine(UIHub hub) : IAudioRecorderEngine
{
    private static readonly string JSSetRecordingMethod = $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setRecording";
    private static readonly string JSSetConnectedMethod = $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setConnected";
    private static readonly string JSSetSignalDetectedMethod = $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setSignalDetected";
    private static readonly string JSSetVoiceActiveMethod = $"{BlazorUIAppModule.ImportName}.RecorderStateHub.setVoiceActive";
    private static readonly string JSOnAudioPowerChangeMethod = $"{BlazorUIAppModule.ImportName}.RecorderStateHub.onAudioPowerChange";
    private static readonly string JSMicrophoneIsCapturedMethod = $"{BlazorUIAppModule.ImportName}.RecorderStateHub.microphoneIsCaptured";

    private ChatId? _chatId;
    private AudioStreamer? _streamer;
    private AudioStreamer.AudioStream? _currentStream;
    private CancellationTokenSource? _recordingCts;
    private bool _isRecording;
    private bool _isConnected;
    private bool _isSignalDetected;
    private bool _isVoiceActive;

    [field: AllowNull, MaybeNull]
    private MicrophonePermissionHandler MicrophonePermissionHandler => field ??= hub.Services.GetRequiredService<MicrophonePermissionHandler>();

    [field: AllowNull, MaybeNull]
    private IAudioCapture AudioCapture => field ??= hub.Services.GetRequiredService<IAudioCapture>();

    [field: AllowNull, MaybeNull]
    private VoiceActivityDetector VoiceActivityDetector => field ??= hub.Services.GetRequiredService<VoiceActivityDetector>();

    [field: AllowNull, MaybeNull]
    private IAudioRecorderBackend AudioRecorderBackend => field ??= hub.Services.GetRequiredService<IAudioRecorderBackend>();

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.LogFor<MauiRecorderEngine>();

    private IJSRuntime JS => hub.JS;

    public async Task<bool> StartAsync(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        // Initialize and connect AudioStreamer
        _streamer ??= new AudioStreamer(hub.HostInfo.BaseUrl);
        await _streamer.EnsureConnected(quickReconnect: true, cancellationToken).ConfigureAwait(false);
        await SetConnected(true).ConfigureAwait(false);

        _recordingCts = cancellationToken.CreateLinkedTokenSource();
        var token = _recordingCts.Token;
        var microphoneStream = await AudioCapture.Capture(cancellationToken).ConfigureAwait(false);
        if (microphoneStream is null) {
            Log.LogWarning("Microphone stream is unavailable");
            await SetRecording(false).ConfigureAwait(false);
            return false;
        }

        // Start recording
        _chatId = chatId;
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
        var recordingCts = _recordingCts;
        _recordingCts = null;
        if (recordingCts != null)
            await recordingCts.CancelAsync();

        var stream = _currentStream;
        if (stream == null) {
            await SetRecording(false).ConfigureAwait(false);
            await SetVoiceActive(false).ConfigureAwait(false);
            return true;
        }

        stream.Complete();
        await stream.DisposeAsync();
        _currentStream = null;
        await SetRecording(false).ConfigureAwait(false);
        await SetVoiceActive(false).ConfigureAwait(false);
        return true;
    }

    public async ValueTask EnsureConnected(bool quickReconnect, CancellationToken cancellationToken)
    {
        _streamer ??= new AudioStreamer(hub.HostInfo.BaseUrl);
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

        return new AudioRecorder.AudioDiagnosticsState {
            HasMicrophonePermission = permissionStatus,
            IsConnected = _currentStream != null || (_streamer?.IsConnected ?? false),
            HasMicrophoneStream = _isSignalDetected,
            LastVadEvent = new AudioRecorder.VadEvent {
                Kind = lastVadEvent.Kind.ToString(),
                Offset = lastVadEvent.OffsetSeconds,
                Duration = lastVadEvent.DurationSeconds ?? 0,
                SpeechProb = lastVadEvent.SpeechProb,
            },
            IsSignalDetected = _isSignalDetected,
            IsVadActive = VoiceActivityDetector.IsInitialized,
        };
    }

    // Private methods

    private ValueTask SetRecording(bool isRecording)
    {
        if (_isRecording == isRecording)
            return ValueTask.CompletedTask;

        _isRecording = isRecording;
        StateHasChanged();
        return JS.InvokeVoidAsync(JSSetRecordingMethod, isRecording);
    }

    private ValueTask SetConnected(bool isConnected)
    {
        if (_isConnected == isConnected)
            return ValueTask.CompletedTask;

        _isConnected = isConnected;
        StateHasChanged();
        return JS.InvokeVoidAsync(JSSetConnectedMethod, isConnected);
    }

    private ValueTask SetSignalDetected(bool isSignalDetected)
    {
        if (_isSignalDetected == isSignalDetected)
            return ValueTask.CompletedTask;

        _isSignalDetected = isSignalDetected;
        StateHasChanged();
        return JS.InvokeVoidAsync(JSSetSignalDetectedMethod, isSignalDetected);
    }

    private ValueTask SetVoiceActive(bool isVoiceActive)
    {
        if (_isVoiceActive == isVoiceActive)
            return ValueTask.CompletedTask;

        _isVoiceActive = isVoiceActive;
        StateHasChanged();
        return JS.InvokeVoidAsync(JSSetVoiceActiveMethod, isVoiceActive);
    }

    private ValueTask OnAudioPowerChange(double power)
        => JS.InvokeVoidAsync(JSOnAudioPowerChangeMethod, power);

    private ValueTask MicrophoneIsCaptured(double gain)
        => JS.InvokeVoidAsync(JSMicrophoneIsCapturedMethod, gain);

    private void StateHasChanged()
        => AudioRecorderBackend.OnRecordingStateChange(_isRecording, _isSignalDetected, _isConnected, _isVoiceActive);

    private async Task RecordingHeartbeat(CancellationToken cancellationToken)
    {
        try {
            var chatId = _chatId;
            if (!_isRecording)
                return;

            if (chatId is null) {
                await StopAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            var isRecording = AudioRecorderBackend.IsRecording(chatId.Value);
            if (isRecording)
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

        var detectSpeechTask = BackgroundTask.Run(async () => {
                await DetectSpeech(vadRingBuffer, vadChannel.Writer, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

        await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            using var _ = frame;
            var memory = frame.Memory;

            var isVadBufferPushed = vadRingBuffer.TryPush(memory.Span);
            var isEncodingBufferPushed = encodingRingBuffer.TryPush(memory.Span);
            if (!isVadBufferPushed || !isEncodingBufferPushed)
                await Task.WhenAll(
                    vadRingBuffer.WhenPulled,
                    encodingRingBuffer.WhenPulled).ConfigureAwait(false);

            var gain = AudioExt.ApproximateGain(memory.Span);
            Log.LogInformation("Got frame {FrameLength} with gain={ApproximateGain}",
                memory.Length,
                gain);

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

    private async Task DetectSpeech(
        BlockRingBuffer<float> buffer,
        ChannelWriter<VoiceActivityChange> vadChannel,
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
                await vadChannel.WriteAsync(vadResult.Change.Value, cancellationToken);
                await SetVoiceActive(vadResult.Change.Value.Kind == VoiceActivityKind.Start).ConfigureAwait(false);
            }
            else if (vad.LastActivityEvent.Kind == VoiceActivityKind.Start)
                // Notify JS about audio power to keep heartbeat and UI animations
                await OnAudioPowerChange(vadResult.Gain).ConfigureAwait(false);
        }
    }

    // private class RecordingProcess(AppUIHub hub): UIWorkerBase<AppUIHub>(hub)
    // {
    //     protected override Task OnRun(CancellationToken cancellationToken)
    //     {
    //         var baseChains = new[] {
    //             AsyncChain.From(InvalidateIsSelectedChatUnlisted),
    //             AsyncChain.From(PlayTuneOnNewMessages),
    //         };
    //         var retryDelays = RetryDelaySeq.Exp(0.1, 1);
    //         return (
    //             from chain in baseChains
    //             select chain
    //                 .Log(LogLevel.Debug, Log)
    //                 .RetryForever(retryDelays, Log)
    //             ).RunIsolated(cancellationToken);
    //     }
    // }

}
