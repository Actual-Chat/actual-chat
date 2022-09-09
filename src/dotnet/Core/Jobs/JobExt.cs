namespace ActualChat.Jobs;

public static class JobExt
{
    public static JobConfiguration<T> Configure<T>(this IJob<T> job)
        => new (job);

    public static JobConfiguration<T> ShardByUserId<T>(this JobConfiguration<T> existingConfiguration, string userId)
        => existingConfiguration with { ShardKind = ShardKind.User, ShardKey = userId };

    public static JobConfiguration<T> ShardByChatId<T>(this JobConfiguration<T> existingConfiguration, string chatId)
        => existingConfiguration with { ShardKind = ShardKind.Chat, ShardKey = chatId };

    public static JobConfiguration<T> WithPriority<T>(this JobConfiguration<T> existingConfiguration, JobPriority priority)
        => existingConfiguration with { Priority = priority };

    public static Task ScheduleNow<T>(
        this JobConfiguration<T> jobConfiguration,
        Jobs jobs,
        CancellationToken cancellationToken)
        => jobs.Schedule(jobConfiguration, cancellationToken);

    // ReSharper disable once UnusedParameter.Global
    public static void ScheduleOnCompletion<T>(this JobConfiguration<T> jobConfiguration, ICommand command)
    {
        var commandContext = CommandContext.GetCurrent();
        commandContext.Operation().Items.Set(jobConfiguration);
    }
}
