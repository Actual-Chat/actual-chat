using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Messaging;
using TimeoutException = System.TimeoutException;

namespace ActualChat.Audio.UI.Blazor.Components;

public record AudioRecorderState(ChatId ChatId) : IHasId<Ulid>
{
    public Ulid Id { get; } = Ulid.NewUlid();
    public bool IsRecording { get; init; }
}

public class AudioRecorder : IAsyncDisposable
{
    private readonly IMessageProcessor<IAudioRecorderCommand> _messageProcessor;
    private readonly IMutableState<AudioRecorderState?> _state;
    private IJSObjectReference? _jsRef;

    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.AudioRecording;
    private Session Session { get; }
    private IJSRuntime Js { get; }

    public Task WhenInitialized { get; }
    public IState<AudioRecorderState?> State => _state;

    public AudioRecorder(IServiceProvider services)
    {
        Log = services.LogFor<AudioRecorder>();
        Session = services.GetRequiredService<Session>();
        Js = services.GetRequiredService<IJSRuntime>();
        _messageProcessor = new MessageProcessor<IAudioRecorderCommand>(ProcessCommand) {
            MaxProcessCallDurationMs = 5000,
        };
        _state = services.StateFactory().NewMutable(
            (AudioRecorderState?)null,
            StateCategories.Get(GetType(), nameof(State)));
        WhenInitialized = Initialize();

        async Task Initialize()
        {
            _jsRef = await Js.InvokeAsync<IJSObjectReference>(
                    $"{AudioBlazorUIModule.ImportName}.AudioRecorder.create",
                    Session.Id)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _messageProcessor.Complete().SuppressExceptions().ConfigureAwait(false);
        await _messageProcessor.DisposeAsync().ConfigureAwait(false);
        await _jsRef.DisposeSilentlyAsync("dispose").ConfigureAwait(false);
        _jsRef = null!;
    }

    public async Task<bool> CanRecord(CancellationToken cancellationToken = default)
    {
        await WhenInitialized.ConfigureAwait(false);
        return await _jsRef!.InvokeAsync<bool>("canRecord", cancellationToken).ConfigureAwait(false);
    }

    public async Task StartRecording(ChatId chatId, CancellationToken cancellationToken = default)
    {
        DebugLog?.LogDebug(nameof(StartRecording) + ": chat #{ChatId}", chatId);

        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        try {
            var messageProcess = _messageProcessor.Enqueue(new StartAudioRecorderCommand(chatId), cancellationToken);
            await messageProcess.WhenCompleted.ConfigureAwait(false);
        }
        catch (TimeoutException e) {
            Log.LogError(e, nameof(StartRecording) + " failed with timeout");
            MarkStopped();
            throw new AudioRecorderException("Unable to start recording in time.", e);
        }
        DebugLog?.LogDebug(nameof(StartRecording) + ": completed for chat #{ChatId}", chatId);
    }

    public async Task StopRecording(CancellationToken cancellationToken = default)
    {
        DebugLog?.LogDebug(nameof(StopRecording));

        var messageProcess = _messageProcessor.Enqueue(StopAudioRecorderCommand.Instance);
        await messageProcess.WhenCompleted.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<object?> ProcessCommand(IAudioRecorderCommand command, CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug(nameof(ProcessCommand));
        switch (command) {
        case StartAudioRecorderCommand startCommand:
            var chatId = startCommand.ChatId;
            await WhenInitialized.ConfigureAwait(false);
            await StartRecordingInternal(chatId, cancellationToken).ConfigureAwait(false);
            return nameof(StartAudioRecorderCommand);
        case StopAudioRecorderCommand:
            if (!WhenInitialized.IsCompletedSuccessfully)
                throw StandardError.StateTransition(GetType(), "Recorder is not initialized yet.");

            await StopRecordingInternal(cancellationToken).ConfigureAwait(false);
            return nameof(StopAudioRecorderCommand);
        default:
            throw StandardError.NotSupported(GetType(), $"Unsupported command type: '{command.GetType()}'.");
        }
    }

    private async Task StartRecordingInternal(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        if (IsRecording)
            return;

        DebugLog?.LogDebug(nameof(StartRecordingInternal) + ": chat #{ChatId}", chatId);
        MarkStarting(chatId);
        try {
            var hasMicrophone = await _jsRef!.InvokeAsync<bool>(
                    "startRecording",
                    cancellationToken,
                    chatId)
                .ConfigureAwait(false);
            if (!hasMicrophone) {
                Log.LogWarning($"{nameof(StartRecordingInternal)}: there is no microphone available");
                // Cancel recording
                MarkStopped();
                throw new AudioRecorderException("Microphone is not ready.");
            }

            MarkRecording();
        }
        catch (Exception e) when (e is not OperationCanceledException && e is not AudioRecorderException) {
            Log.LogError(e, $"{nameof(StartRecordingInternal)}: start recording failed");
            MarkStopped();
            throw new AudioRecorderException("Voice recording failed.", e);
        }
    }

    private bool IsRecording => _state.Value is { IsRecording: true };

    private async Task StopRecordingInternal(CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug(nameof(StopRecordingInternal));

        try {
            await _jsRef!.InvokeVoidAsync("stopRecording", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, $"{nameof(StopRecordingInternal)}: stop recording failed");
            throw;
        }
        finally {
            MarkStopped();
        }
    }

    private void MarkStarting(ChatId chatId)
        => _state.Value = new AudioRecorderState(chatId);

    private void MarkRecording()
        => _state.Value = _state.Value! with { IsRecording = true };

    private void MarkStopped()
        => _state.Value = null;
}
