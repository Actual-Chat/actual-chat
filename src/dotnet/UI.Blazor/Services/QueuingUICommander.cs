namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Sends every command through <see cref="CommandQueueUI"/>: a UI action lasts until the command gets through,
/// and fails only once the queue gives up on it.
/// </summary>
public sealed class QueuingUICommander(IServiceProvider services) : UICommander(services)
{
    private CommandQueueUI CommandQueueUI => field ??= Services.GetRequiredService<CommandQueueUI>();

    // Protected/internal methods

    protected override UIAction<TResult> CreateUIAction<TResult>(
        ICommand command,
        CancellationToken cancellationToken)
    {
        var runTask = CommandQueueUI.Run(command, CommandSource.UICommander, cancellationToken);
        var resultTask = GetResult<TResult>(runTask);
        return new UIAction<TResult>((ICommand<TResult>)command, Clock, resultTask, cancellationToken);
    }

    // Private methods

    private static async Task<TResult> GetResult<TResult>(Task<object?> resultTask)
    {
        var result = await resultTask.ConfigureAwait(false);
        // A replaced command ends with no result, which a value-typed TResult can't be cast from
        return result is null ? default! : (TResult)result;
    }
}
