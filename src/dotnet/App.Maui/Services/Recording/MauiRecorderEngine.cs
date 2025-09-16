using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services.Recording;

public class MauiRecorderEngine(UIHub hub) : IAudioRecorderEngine
{
    private AudioStreamer? _streamer;
    private AudioStreamer.AudioStream? _currentStream;
    private CancellationTokenSource? _recordingCts;

    [field: AllowNull, MaybeNull]
    private MicrophonePermissionHandler MicrophonePermissionHandler => field ??= hub.Services.GetRequiredService<MicrophonePermissionHandler>();

    [field: AllowNull, MaybeNull]
    private IAudioCapture AudioCapture => field ??= hub.Services.GetRequiredService<IAudioCapture>();

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
        => ValueTask.CompletedTask;

    public async Task<AudioRecorder.AudioDiagnosticsState> RunDiagnostics(CancellationToken cancellationToken)
    {
        var permissionStatus = await MicrophonePermissionHandler.Check(cancellationToken);

        return new AudioRecorder.AudioDiagnosticsState {
            HasMicrophonePermission = permissionStatus,
            IsConnected = _currentStream != null || (_streamer?.IsConnected ?? false),
        };
    }

    private async Task ProcessMicrophoneStream(IAsyncEnumerable<ReadOnlyMemory<float>> frames, CancellationToken cancellationToken)
    {
        await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false))
            Log.LogInformation("Got frame {FrameLength} with gain={ApproximateGain}", frame.Length, AudioExt.ApproximateGain(frame.Span));
    }
}
