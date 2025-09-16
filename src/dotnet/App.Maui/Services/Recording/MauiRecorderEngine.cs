using System.Buffers;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services.Recording;

public class MauiRecorderEngine(UIHub hub) : IAudioRecorderEngine
{
    private AudioStreamer? _streamer;
    private AudioStreamer.AudioStream? _currentStream;
    private CancellationTokenSource? _recordingCts;

    private BlockRingBuffer<float>? _vadRingBuffer;
    private BlockRingBuffer<float>? _encodingRingBuffer;

    [field: AllowNull, MaybeNull]
    private MicrophonePermissionHandler MicrophonePermissionHandler => field ??= hub.Services.GetRequiredService<MicrophonePermissionHandler>();

    [field: AllowNull, MaybeNull]
    private IAudioCapture AudioCapture => field ??= hub.Services.GetRequiredService<IAudioCapture>();

    [field: AllowNull, MaybeNull]
    private VoiceActivityDetector VoiceActivityDetector => field ??= hub.Services.GetRequiredService<VoiceActivityDetector>();

    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= hub.LogFor<MauiRecorderEngine>();

    public async Task<bool> StartAsync(
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        // Initialize and connect AudioStreamer
        _streamer ??= new AudioStreamer(hub.HostInfo.BaseUrl);
        await _streamer.EnsureConnected(quickReconnect: true, cancellationToken).ConfigureAwait(false);

        _recordingCts = cancellationToken.CreateLinkedTokenSource();
        var token = _recordingCts.Token;
        var microphoneStream = await AudioCapture.Capture(cancellationToken).ConfigureAwait(false);
        if (microphoneStream is null) {
            Log.LogWarning("Microphone stream is unavailable");
            return false;
        }

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
        if (stream == null)
            return true;

        stream.Complete();
        await stream.DisposeAsync();
        _currentStream = null;
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

        return new AudioRecorder.AudioDiagnosticsState {
            HasMicrophonePermission = permissionStatus,
            IsConnected = _currentStream != null || (_streamer?.IsConnected ?? false),
        };
    }

    private async Task ProcessMicrophoneStream(IAsyncEnumerable<IMemoryOwner<float>> frames, CancellationToken cancellationToken)
    {
        var vadRingBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10); // 10 seconds of VAD buffer
        var encodingRingBuffer = new BlockRingBuffer<float>(Constants.Audio.RecordingSampleRate * 10); // 10 seconds of encoding buffer
        await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            using var _ = frame;
            var memory = frame.Memory;

            var isVadBufferPushed = vadRingBuffer.TryPush(memory.Span);
            var isEncodingBufferPushed = encodingRingBuffer.TryPush(memory.Span);
            if (!isVadBufferPushed || !isEncodingBufferPushed)
                await Task.WhenAll(
                    vadRingBuffer.WhenPulled,
                    encodingRingBuffer.WhenPulled).ConfigureAwait(false);
            Log.LogInformation("Got frame {FrameLength} with gain={ApproximateGain}",
                memory.Length,
                AudioExt.ApproximateGain(memory.Span));
        }
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
            if (vadResult.HasEvent)
                await vadChannel.WriteAsync(vadResult.Change.Value, cancellationToken);

            // TODO: Send gain to the JS side for button animation
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
