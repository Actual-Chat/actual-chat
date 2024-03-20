
using Microsoft.Extensions.Hosting;

namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

internal static class WorkerPoolServicesExt
{
    public static IServiceCollection AddWorkerPoolDependencies(this IServiceCollection services)
    {
        services.AddSingleton(typeof(IShardIndexResolver<>), typeof(ShardIndexResolver<>));
        services.AddSingleton(typeof(IWorkerPoolShardFactory<,,,>), typeof(WorkerPoolShardFactory<,,,>));

        return services;
    }

    public static IServiceCollection AddWorkerPool<TService, TImplementation, TJob, TJobId, TShardKey>(
        this IServiceCollection services,
        DuplicateJobPolicy duplicateJobPolicy,
        int shardConcurrencyLevel
    )
        where TService : class, IWorker<TJob>
        where TImplementation : class, TService
        where TJob : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
        where TJobId : notnull
        where TShardKey : notnull
    {
        services.AddSingleton(services
            => services.CreateInstanceWith<WorkerPool<TService, TJob, TJobId, TShardKey>>(
                duplicateJobPolicy, shardConcurrencyLevel))
            .AddAlias<IWorkerPool<TJob, TJobId, TShardKey>, WorkerPool<TService, TJob, TJobId, TShardKey>>()
            .AddAlias<IHostedService, WorkerPool<TService, TJob, TJobId, TShardKey>>();

        services.AddSingleton<TService, TImplementation>();

        return services;
    }
}
