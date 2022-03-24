using ActualChat.Commands;

namespace ActualChat.Audio.UI.Blazor.Components;

public interface IAudioRecorderCommand : IQueueCommand {}

public sealed record StartAudioRecorderCommand(Symbol ChatId, CancellationToken CancellationToken)
    : IAudioRecorderCommand;

public sealed class StopAudioRecorderCommand : IAudioRecorderCommand
{
    public static readonly StopAudioRecorderCommand Instance = new();
    private StopAudioRecorderCommand() { }

    /// <summary> You can't cancel stop command. </summary>
    public CancellationToken CancellationToken => default;
}

public class AudioRecorderCommandProcessor : CommandProcessor<IAudioRecorderCommand>
{
    private readonly AudioRecorder _recorder;

    public AudioRecorderCommandProcessor(AudioRecorder recorder)
        => _recorder = recorder;

    public override async Task ProcessCommand(
        IAudioRecorderCommand command,
        CancellationToken cancellationToken)
    {
        switch (command) {
            case StartAudioRecorderCommand startCommand:
                await OnStartCommand(startCommand.ChatId).ConfigureAwait(false);
                break;
            case StopAudioRecorderCommand:
                await OnStopCommand().ConfigureAwait(false);
                break;
            default:
                throw new NotSupportedException($"Unsupported command type: '{command.GetType()}'.");
        }

        async ValueTask OnStartCommand(Symbol chatId)
        {
            await _recorder.Initialization.ConfigureAwait(false);
            await _recorder.StartRecording(chatId).ConfigureAwait(false);
        }

        async Task OnStopCommand()
        {
            if (!_recorder.Initialization.IsCompletedSuccessfully)
                throw new LifetimeException("Recorder is not initialized yet.");
            await _recorder.StopRecording().ConfigureAwait(false);
        }
    }
}

public class AudioRecorderController : IAsyncDisposable
{
    private readonly CommandQueue<IAudioRecorderCommand> _commandQueue;

    public AudioRecorderController(CommandQueueFactory commandQueueFactory)
        => _commandQueue = commandQueueFactory.Create<IAudioRecorderCommand, AudioRecorderCommandProcessor>();

    public async Task<CommandExecution> StartRecording(string chatId)
    {
        var startCommand = new StartAudioRecorderCommand(chatId, default);
        return await _commandQueue.EnqueueCommand(startCommand).ConfigureAwait(false);
    }

    public async Task<CommandExecution> StopRecording()
        => await _commandQueue.EnqueueCommand(StopAudioRecorderCommand.Instance).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
        => await _commandQueue.DisposeAsync().ConfigureAwait(false);
}
