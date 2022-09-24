namespace ActualChat.Commands;

public static class CommandExt
{
    public static ValueTask Enqueue(this IEvent @event, CancellationToken cancellationToken)
        => Enqueue((IBackendCommand)@event, cancellationToken);

    public static ValueTask Enqueue(this IEvent @event, QueueRef queueRef, CancellationToken cancellationToken)
        => Enqueue((IBackendCommand)@event, queueRef, cancellationToken);

    public static ValueTask Enqueue(
        this IEvent @event,
        QueueRef queueRef1,
        QueueRef queueRef2,
        CancellationToken cancellationToken)
        => Enqueue((IBackendCommand)@event, queueRef1, queueRef2, cancellationToken);

    public static ValueTask Enqueue(this IBackendCommand command, CancellationToken cancellationToken)
    {
        var commandContext = CommandContext.GetCurrent();
        var provider = commandContext.Services.GetRequiredService<ICommandQueueProvider>();
        var queue = provider.Get(QueueRef.Default);
        return queue.Enqueue(command, cancellationToken);
    }

    public static ValueTask Enqueue(
        this IBackendCommand command,
        QueueRef queueRef,
        CancellationToken cancellationToken)
    {
        var commandContext = CommandContext.GetCurrent();
        var provider = commandContext.Services.GetRequiredService<ICommandQueueProvider>();
        var queue = provider.Get(queueRef);
        return queue.Enqueue(command, cancellationToken);
    }

    public static ValueTask Enqueue(
        this IBackendCommand command,
        QueueRef queueRef1,
        QueueRef queueRef2,
        CancellationToken cancellationToken)
    {
        var commandContext = CommandContext.GetCurrent();
        var provider = commandContext.Services.GetRequiredService<ICommandQueueProvider>();
        var queue1 = provider.Get(queueRef1);
        var queue2 = provider.Get(queueRef2);
        var enqueue1 = queue1.Enqueue(command, cancellationToken);
        var enqueue2 = queue2.Enqueue(command, cancellationToken);

        return TaskExt.WhenAll(enqueue1, enqueue2);
    }

    public static ValueTask EnqueueOnCompletion(this IEvent @event, ICommand after, CancellationToken cancellationToken)
        => EnqueueOnCompletion((IBackendCommand)@event, after, cancellationToken);

    public static ValueTask EnqueueOnCompletion(
        this IEvent @event,
        ICommand after,
        QueueRef queueRef,
        CancellationToken cancellationToken)
        => EnqueueOnCompletion((IBackendCommand)@event, after, queueRef, cancellationToken);

    public static ValueTask EnqueueOnCompletion(
        this IEvent @event,
        ICommand after,
        QueueRef queueRef1,
        QueueRef queueRef2,
        CancellationToken cancellationToken)
        => EnqueueOnCompletion((IBackendCommand)@event,
            after,
            queueRef1,
            queueRef2,
            cancellationToken);

    public static ValueTask EnqueueOnCompletion(
        this IBackendCommand command,
        ICommand after,
        CancellationToken cancellationToken)
    {
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            return ValueTask.CompletedTask;

        if (!ReferenceEquals(commandContext.UntypedCommand, after))
            throw StandardError.Constraint<ICommand>("doesn't handled by current CommandContext.");

        commandContext.OutermostContext.Operation()
            .Items.Set(new QueuedCommand(command, ImmutableArray.Create(QueueRef.Default)));
        return ValueTask.CompletedTask;
    }

    public static ValueTask EnqueueOnCompletion(
        this IBackendCommand command,
        ICommand after,
        QueueRef queueRef,
        CancellationToken cancellationToken)
    {
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            return ValueTask.CompletedTask;

        if (!ReferenceEquals(commandContext.UntypedCommand, after))
            throw StandardError.Constraint<ICommand>("doesn't handled by current CommandContext.");

        commandContext.OutermostContext.Operation()
            .Items.Set(new QueuedCommand(command, ImmutableArray.Create(queueRef)));
        return ValueTask.CompletedTask;
    }

    public static ValueTask EnqueueOnCompletion(
        this IBackendCommand command,
        ICommand after,
        QueueRef queueRef1,
        QueueRef queueRef2,
        CancellationToken cancellationToken)
    {
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            return ValueTask.CompletedTask;

        if (!ReferenceEquals(commandContext.UntypedCommand, after))
            throw StandardError.Constraint<ICommand>("doesn't handled by current CommandContext.");

        commandContext.OutermostContext.Operation()
            .Items.Set(new QueuedCommand(command, ImmutableArray.Create(queueRef1, queueRef2)));
        return ValueTask.CompletedTask;
    }
}
