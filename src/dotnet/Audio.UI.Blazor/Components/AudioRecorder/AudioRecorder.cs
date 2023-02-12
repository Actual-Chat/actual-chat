using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Messaging;
using TimeoutException = System.TimeoutException;

namespace ActualChat.Audio.UI.Blazor.Components;

public record AudioRecorderState(ChatId ChatId) : IHasId<Ulid>
{
    public Ulid Id { get; } = Ulid.NewUlid();
    public AudioRecorderError? Error { get; init; }
    public bool IsRecording { get; init; }
}

public enum AudioRecorderError
{
    Microphone = 1,
    Generic = 2,
    Timeout = 4,
}

public class AudioRecorder : IAudioRecorderBackend, IAsyncDisposable
{
    private readonly IMessageProcessor<IAudioRecorderCommand> _messageProcessor;
    private IJSObjectReference? _jsRef;
    private DotNetObjectReference<IAudioRecorderBackend>? _blazorRef;

    private ILogger<AudioRecorder> Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.AudioRecording;
    private Session Session { get; }
    private IJSRuntime Js { get; }

    public Task WhenInitialized { get; }
    public IMutableState<AudioRecorderState?> State { get; }

    public AudioRecorder(
        ILogger<AudioRecorder> log,
        Session session,
        IStateFactory stateFactory,
        IJSRuntime js)
    {
        Log = log;
        Session = session;
        Js = js;
        _messageProcessor = new MessageProcessor<IAudioRecorderCommand>(ProcessCommand);
        State = stateFactory.NewMutable(
            (AudioRecorderState?)null,
            StateCategories.Get(GetType(), nameof(State)));
        WhenInitialized = Initialize();

        async Task Initialize()
        {
            _blazorRef = DotNetObjectReference.Create<IAudioRecorderBackend>(this);
            _jsRef = await Js.InvokeAsync<IJSObjectReference>(
                $"{AudioBlazorUIModule.ImportName}.AudioRecorder.create",
                _blazorRef,
                Session.Id);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _messageProcessor.Complete().SuppressExceptions();
        await _messageProcessor.DisposeAsync();
        await StopRecordingInternal(CancellationToken.None);
        await _jsRef.DisposeSilentlyAsync("dispose");
        _jsRef = null!;
        _blazorRef.DisposeSilently();
        _blazorRef = null!;
    }

    public async Task<bool> CanRecord(CancellationToken cancellationToken = default)
    {
        await WhenInitialized;
        return await _jsRef!.InvokeAsync<bool>("canRecord", cancellationToken);
    }

    public async Task StartRecording(ChatId chatId, CancellationToken cancellationToken = default)
    {
        DebugLog?.LogDebug(nameof(StartRecording) + ": chat #{ChatId}", chatId);

        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        var recordingCts = cancellationToken.CreateLinkedTokenSource();
        try {
            _messageProcessor.Enqueue(new StartAudioRecorderCommand(chatId, recordingCts), recordingCts.Token);

            await State.When(state => state?.IsRecording ?? false, recordingCts.Token)
                .WaitAsync(TimeSpan.FromSeconds(5), recordingCts.Token);
        }
        catch (TimeoutException e) {
            Log.LogWarning(e, nameof(StartRecording) + " failed with timeout");
            recordingCts.Cancel();
            var currentValue = State.Value;
            if (currentValue is { Error: null })
                State.Value = currentValue with { Error = AudioRecorderError.Timeout };
        }
        DebugLog?.LogDebug(nameof(StartRecording) + ": completed for chat #{ChatId}", chatId);
    }

    public async Task StopRecording(CancellationToken cancellationToken = default)
    {
        DebugLog?.LogDebug(nameof(StopRecording));

        _messageProcessor.Enqueue(StopAudioRecorderCommand.Instance);

        await State.When(state => state == null, cancellationToken)
           .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
    }

    private async Task<object?> ProcessCommand(IAudioRecorderCommand command, CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug(nameof(ProcessCommand));
        switch (command) {
        case StartAudioRecorderCommand startCommand:
            var (chatId, recordingCancellation) = startCommand;
            await WhenInitialized;
            await StartRecordingInternal(chatId, recordingCancellation, cancellationToken);
            return nameof(StartAudioRecorderCommand);
        case StopAudioRecorderCommand:
            if (!WhenInitialized.IsCompletedSuccessfully)
                throw StandardError.StateTransition(GetType(), "Recorder is not initialized yet.");

            await StopRecordingInternal(cancellationToken);
            return nameof(StopAudioRecorderCommand);
        default:
            throw StandardError.NotSupported(GetType(), $"Unsupported command type: '{command.GetType()}'.");
        }
    }

    private async Task StartRecordingInternal(
        ChatId chatId,
        CancellationTokenSource recordingCancellation,
        CancellationToken cancellationToken)
    {
        if (State.Value != null)
            return;

        DebugLog?.LogDebug(nameof(StartRecordingInternal) + ": chat #{ChatId}", chatId);
        State.Value = new AudioRecorderState(chatId);
        try {
            if (_jsRef != null) {
                var hasMicrophone = await _jsRef.InvokeAsync<bool>(
                        "startRecording",
                        cancellationToken,
                        chatId)
                    .ConfigureAwait(false);
                if (!hasMicrophone) {
                    Log.LogWarning($"{nameof(StartRecordingInternal)}: there is no microphone available");
                    // Cancel recording
                    State.Value = State.Value with { Error = AudioRecorderError.Microphone };
                    recordingCancellation.Cancel();
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, $"{nameof(StartRecordingInternal)}: start recording failed");
            State.Value = State.Value with { Error = AudioRecorderError.Generic };
            recordingCancellation.Cancel();
            throw;
        }
    }

    private async Task StopRecordingInternal(CancellationToken cancellationToken)
    {
        if (State.Value == null)
            return;

        DebugLog?.LogDebug(nameof(StopRecordingInternal));

        var state = State.Value;
        _ = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)
            .ContinueWith(_ => {
                if (State.Value?.Id != state?.Id)
                    return; // We don't want to stop the next recording here

                Log.LogWarning(nameof(OnRecordingStopped) + " wasn't invoked on time by JS backend");
                OnRecordingStopped();
            }, TaskScheduler.Current);

        if (_jsRef != null)
            await _jsRef.InvokeVoidAsync("stopRecording", cancellationToken);
    }

    // JS backend callback handlers

    [JSInvokable]
    public void OnRecordingStarted(string chatId)
    {
        var recorderState = State.Value;
        if (recorderState is not { Error: null })
            return;

        DebugLog?.LogDebug(nameof(OnRecordingStarted) + ": chat #{ChatId}", chatId);
        State.Value = recorderState with { IsRecording = true };
    }

    [JSInvokable]
    public void OnRecordingStopped()
    {
        // Does the same as StopRecording; we assume here that recording
        // might be recognized as stopped by JS backend as well
        if (State.Value == null)
            return;

        DebugLog?.LogDebug(nameof(OnRecordingStopped));
        State.Value = null;
    }
}
