namespace ActualChat.ScheduledCommands;

public static class CommandExt
{
    public static CommandConfiguration Configure(this IEvent @event)
        => new (@event);

    public static CommandConfiguration Configure(this IBackendCommand command)
        => new (command);

    public static CommandConfiguration ShardByUserId(this CommandConfiguration existingConfiguration, Symbol userId)
        => existingConfiguration with { ShardKind = ShardKind.User, ShardKey = userId };

    public static CommandConfiguration ShardByChatId(this CommandConfiguration existingConfiguration, Symbol chatId)
        => existingConfiguration with { ShardKind = ShardKind.Chat, ShardKey = chatId };

    public static CommandConfiguration WithPriority(this CommandConfiguration existingConfiguration, CommandPriority priority)
        => existingConfiguration with { Priority = priority };

    public static Task ScheduleNow(
        this CommandConfiguration commandConfiguration,
        CancellationToken cancellationToken)
    {
        var commandContext = CommandContext.GetCurrent();
        var jobs = commandContext.Services.GetRequiredService<CommandGateway>();
        return jobs.Schedule(commandConfiguration, cancellationToken);
    }

    public static Task ScheduleNow(
        this CommandConfiguration commandConfiguration,
        CommandGateway commandGateway,
        CancellationToken cancellationToken)
        => commandGateway.Schedule(commandConfiguration, cancellationToken);

    // ReSharper disable once UnusedParameter.Global
    public static Task ScheduleOnCompletion(
        this CommandConfiguration commandConfiguration,
        ICommand command,
        CancellationToken cancellationToken)
    {
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            return Task.CompletedTask;

        commandContext.Operation().Items.Set(commandConfiguration);
        return Task.CompletedTask;
    }

    // ReSharper disable once UnusedParameter.Global
    public static Task ScheduleOnCompletion(
        this IBackendCommand command,
        ICommand after,
        CancellationToken cancellationToken)
    {
        var commandContext = CommandContext.GetCurrent();
        var eventConfiguration = command.Configure();
        commandContext.Operation().Items.Set(eventConfiguration);
        return Task.CompletedTask;
    }
}
