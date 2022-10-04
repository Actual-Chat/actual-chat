using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.Messaging;

namespace ActualChat.Audio.UI.Blazor.Components;

public record AudioRecorderState(Symbol ChatId) : IHasId<Ulid>
{
    public Ulid Id { get; init; } = Ulid.NewUlid();
    public bool IsRecording { get; init; }
}

public class AudioRecorder : IAudioRecorderBackend, IAsyncDisposable
{
    private readonly ILogger<AudioRecorder> _log;
    private ILogger? DebugLog => DebugMode ? _log : null;
    private bool DebugMode => Constants.DebugMode.AudioRecording;

    private readonly Session _session;
    private readonly IJSRuntime _js;
    private readonly IMessageProcessor<IAudioRecorderCommand> _messageProcessor;
    private IJSObjectReference? JSRef { get; set; }
    private DotNetObjectReference<IAudioRecorderBackend>? BlazorRef { get; set; }

    public Task WhenInitialized { get; }
    public IMutableState<AudioRecorderState?> State { get; }
    public bool IsRecordingStarted => State.ValueOrDefault != null;

    public AudioRecorder(
        ILogger<AudioRecorder> log,
        Session session,
        IStateFactory stateFactory,
        IJSRuntime js)
    {
        _log = log;
        _session = session;
        _js = js;
        _messageProcessor = new MessageProcessor<IAudioRecorderCommand>(ProcessCommand);
        State = stateFactory.NewMutable<AudioRecorderState?>();
        WhenInitialized = Initialize();

        async Task Initialize()
        {
            BlazorRef = DotNetObjectReference.Create<IAudioRecorderBackend>(this);
            JSRef = await _js.InvokeAsync<IJSObjectReference>(
                $"{AudioBlazorUIModule.ImportName}.AudioRecorder.create",
                BlazorRef, _session.Id, DebugMode);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _messageProcessor.Complete().SuppressExceptions();
        await _messageProcessor.DisposeAsync();
        await StopRecordingInternal();
        await JSRef.DisposeSilentlyAsync("dispose");
        JSRef = null!;
        BlazorRef.DisposeSilently();
        BlazorRef = null!;
    }

    public async Task<bool> CanRecord()
    {
        await WhenInitialized;
        return await JSRef!.InvokeAsync<bool>("canRecord");
    }

    public IMessageProcess<StartAudioRecorderCommand> StartRecording(Symbol chatId, CancellationToken cancellationToken = default)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));
        return _messageProcessor.Enqueue(new StartAudioRecorderCommand(chatId), cancellationToken);
    }

    public IMessageProcess<StopAudioRecorderCommand> StopRecording()
        => _messageProcessor.Enqueue(StopAudioRecorderCommand.Instance);

    private async Task<object?> ProcessCommand(IAudioRecorderCommand command, CancellationToken cancellationToken)
    {
        switch (command) {
        case StartAudioRecorderCommand startCommand:
            var chatId = startCommand.ChatId;
            await WhenInitialized;
            await StartRecordingInternal(chatId);
            break;
        case StopAudioRecorderCommand:
            if (!WhenInitialized.IsCompletedSuccessfully)
                throw StandardError.StateTransition(GetType(), "Recorder is not initialized yet.");

            await StopRecordingInternal();
            break;
        default:
            throw StandardError.NotSupported(GetType(), $"Unsupported command type: '{command.GetType()}'.");
        }
        return null;
    }

    private async Task StartRecordingInternal(string chatId)
    {
        if (IsRecordingStarted) return;
        DebugLog?.LogDebug(nameof(StartRecordingInternal) + ": chat #{ChatId}", chatId);

        State.Value = new AudioRecorderState(chatId);
        if (JSRef != null)
            await JSRef.InvokeVoidAsync("startRecording", chatId).ConfigureAwait(false);
    }

    private async Task StopRecordingInternal()
    {
        if (!IsRecordingStarted) return;
        DebugLog?.LogDebug(nameof(StopRecordingInternal));

        var state = State.Value;
        _ = Task.Delay(TimeSpan.FromSeconds(5))
            .ContinueWith(async _ => {
                if (State.Value?.Id != state?.Id)
                    return; // We don't want to stop the next recording here

                _log.LogWarning(nameof(OnRecordingStopped) + " wasn't invoked on time by JS backend");
                await OnRecordingStopped();
            }, TaskScheduler.Current);

        if (JSRef != null)
            await JSRef.InvokeVoidAsync("stopRecording");
    }

    // JS backend callback handlers

    [JSInvokable]
    public void OnRecordingStarted(string chatId)
    {
        if (!IsRecordingStarted) return;
        DebugLog?.LogDebug(nameof(OnRecordingStarted) + ": chat #{ChatId}", chatId);

        State.Value = (State.Value ?? new AudioRecorderState(chatId)) with { IsRecording = true };
    }

    [JSInvokable]
    public Task OnRecordingStopped()
    {
        // Does the same as StopRecording; we assume here that recording
        // might be recognized as stopped by JS backend as well
        if (!IsRecordingStarted) return Task.CompletedTask;
        DebugLog?.LogDebug(nameof(OnRecordingStopped));

        State.Value = null;
        return Task.CompletedTask;
    }
}
