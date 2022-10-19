using ActualChat.Commands.Internal;

namespace ActualChat.Commands;

public static class CommandExt
{
    public static Task Enqueue(
        this ICommand command,
        QueueRef queueRef,
        CancellationToken cancellationToken = default)
    {
        var commandContext = CommandContext.GetCurrent();
        var provider = commandContext.Services.GetRequiredService<ICommandQueues>();
        var queue = provider.Get(queueRef);
        return queue.Enqueue(command, cancellationToken);
    }

    public static Task Enqueue(
        this ICommand command,
        QueueRef queueRef1,
        QueueRef queueRef2,
        CancellationToken cancellationToken = default)
    {
        var commandContext = CommandContext.GetCurrent();
        var provider = commandContext.Services.GetRequiredService<ICommandQueues>();
        var queue1 = provider.Get(queueRef1);
        var queue2 = provider.Get(queueRef2);
        var task1 = queue1.Enqueue(command, cancellationToken);
        var task2 = queue2.Enqueue(command, cancellationToken);
        return Task.WhenAll(task1, task2);
    }

    public static Task Enqueue(
        this ICommand command,
        QueueRef queueRef1,
        QueueRef queueRef2,
        QueueRef queueRef3,
        CancellationToken cancellationToken = default)
    {
        var commandContext = CommandContext.GetCurrent();
        var provider = commandContext.Services.GetRequiredService<ICommandQueues>();
        var queue1 = provider.Get(queueRef1);
        var queue2 = provider.Get(queueRef2);
        var queue3 = provider.Get(queueRef3);
        var task1 = queue1.Enqueue(command, cancellationToken);
        var task2 = queue2.Enqueue(command, cancellationToken);
        var task3 = queue3.Enqueue(command, cancellationToken);
        return Task.WhenAll(task1, task2, task3);
    }

    public static async Task Enqueue(
        this ICommand command,
        CancellationToken cancellationToken,
        params QueueRef[] queueRefs)
    {
        if (queueRefs.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(queueRefs));

        var commandContext = CommandContext.GetCurrent();
        var provider = commandContext.Services.GetRequiredService<ICommandQueues>();
        var tasks = queueRefs.Select(queueRef => provider.Get(queueRef).Enqueue(command, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public static void EnqueueOnCompletion(this ICommand command, QueueRef queueRef)
    {
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            throw StandardError.Internal("The operation is already completed.");

        var operationItems = GetOperation(commandContext).Items;
        var list = operationItems.GetOrDefault(ImmutableList<EnqueuedCommandEntry>.Empty);
        list = list.Add(new(command, queueRef));
        operationItems.Set(list);
    }

    public static void EnqueueOnCompletion(
        this ICommand command,
        QueueRef queueRef1,
        QueueRef queueRef2)
    {
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            throw StandardError.Internal("The operation is already completed.");

        var operationItems = GetOperation(commandContext).Items;
        var list = operationItems.GetOrDefault(ImmutableList<EnqueuedCommandEntry>.Empty);
        list = list.Add(new(command, queueRef1)).Add(new(command, queueRef2));
        operationItems.Set(list);
    }

    public static void EnqueueOnCompletion(
        this ICommand command,
        QueueRef queueRef1,
        QueueRef queueRef2,
        QueueRef queueRef3)
    {
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            throw StandardError.Internal("The operation is already completed.");

        var operationItems = GetOperation(commandContext).Items;
        var list = operationItems.GetOrDefault(ImmutableList<EnqueuedCommandEntry>.Empty);
        list = list.Add(new(command, queueRef1)).Add(new(command, queueRef2)).Add(new(command, queueRef3));
        operationItems.Set(list);
    }

    // Private methods

    private static IOperation GetOperation(CommandContext? commandContext)
    {
        while (commandContext != null) {
            if (commandContext.Items.TryGet<IOperation>(out var operation))
                return operation;
            commandContext = commandContext.OuterContext;
        }
        throw StandardError.Internal("No operation is running.");
    }
}
