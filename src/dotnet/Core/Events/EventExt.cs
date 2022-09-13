namespace ActualChat.Events;

public static class EventExt
{
    public static EventConfiguration Configure(this IEvent @event)
        => new (@event);

    public static EventConfiguration ShardByUserId(this EventConfiguration existingConfiguration, Symbol userId)
        => existingConfiguration with { ShardKind = ShardKind.User, ShardKey = userId };

    public static EventConfiguration ShardByChatId(this EventConfiguration existingConfiguration, Symbol chatId)
        => existingConfiguration with { ShardKind = ShardKind.Chat, ShardKey = chatId };

    public static EventConfiguration WithPriority(this EventConfiguration existingConfiguration, EventPriority priority)
        => existingConfiguration with { Priority = priority };

    public static Task ScheduleNow(
        this EventConfiguration eventConfiguration,
        CancellationToken cancellationToken)
    {
        var commandContext = CommandContext.GetCurrent();
        var jobs = commandContext.Services.GetRequiredService<EventGateway>();
        return jobs.Schedule(eventConfiguration, cancellationToken);
    }

    public static Task ScheduleNow(
        this EventConfiguration eventConfiguration,
        EventGateway eventGateway,
        CancellationToken cancellationToken)
        => eventGateway.Schedule(eventConfiguration, cancellationToken);

    // ReSharper disable once UnusedParameter.Global
    public static Task ScheduleOnCompletion(this EventConfiguration eventConfiguration, ICommand command)
    {
        var commandContext = CommandContext.GetCurrent();
        if (Computed.IsInvalidating())
            return Task.CompletedTask;

        commandContext.Operation().Items.Set(eventConfiguration);
        return Task.CompletedTask;
    }

    // ReSharper disable once UnusedParameter.Global
    public static Task ScheduleOnCompletion(this IEvent @event, ICommand command)
    {
        var commandContext = CommandContext.GetCurrent();
        var eventConfiguration = @event.Configure();
        commandContext.Operation().Items.Set(eventConfiguration);
        return Task.CompletedTask;
    }
}
