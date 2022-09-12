namespace ActualChat.Jobs;

public static class JobExt
{
    public static JobConfiguration Configure(this IJob job)
        => new (job);

    public static JobConfiguration ShardByUserId(this JobConfiguration existingConfiguration, Symbol userId)
        => existingConfiguration with { ShardKind = ShardKind.User, ShardKey = userId };

    public static JobConfiguration ShardByChatId(this JobConfiguration existingConfiguration, Symbol chatId)
        => existingConfiguration with { ShardKind = ShardKind.Chat, ShardKey = chatId };

    public static JobConfiguration WithPriority(this JobConfiguration existingConfiguration, JobPriority priority)
        => existingConfiguration with { Priority = priority };

    public static Task ScheduleNow(
        this JobConfiguration jobConfiguration,
        Jobs jobs,
        CancellationToken cancellationToken)
        => jobs.Schedule(jobConfiguration, cancellationToken);

    // ReSharper disable once UnusedParameter.Global
    public static void ScheduleOnCompletion(this JobConfiguration jobConfiguration, ICommand command)
    {
        var commandContext = CommandContext.GetCurrent();
        commandContext.Operation().Items.Set(jobConfiguration);
    }
}
