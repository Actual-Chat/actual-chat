using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Jobs;

public static class FusionBuilderExt
{
    public static FusionBuilder AddJobScheduler(this FusionBuilder builder)
    {
        builder.Services.TryAddSingleton<LocalJobQueue>();
        builder.Services.TryAddSingleton<JobScheduler>();
        builder.Services.TryAddSingleton<Jobs>();
        builder.Services.AddHostedService<LocalJobRunner>();
        return builder;
    }
}
