namespace ActualChat.Jobs;

public static class ServiceCollectionExt
{
    public static void AddJobs(this IServiceCollection services)
    {
        services.AddSingleton<IJobsRunner, JobsRunner>();
        services.AddSingleton<JobsWorker>()
            .AddHostedService(c => c.GetRequiredService<JobsWorker>());
    }
}
