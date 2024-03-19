
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

    public static IServiceCollection AddWorkerPool<TService, TImplementation, TCommand, TJobId, TShardKey>(
        this IServiceCollection services,
        DuplicateJobPolicy duplicateJobPolicy,
        int shardConcurrencyLevel
    )
        where TService : class, IWorker<TCommand>
        where TImplementation : class, TService
        where TCommand : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
        where TJobId : notnull
        where TShardKey : notnull
    {
        services.AddSingleton(services
            => services.CreateInstanceWith<WorkerPool<TService, TCommand, TJobId, TShardKey>>(
                duplicateJobPolicy, shardConcurrencyLevel))
            .AddAlias<IWorkerPool<TCommand>, WorkerPool<TService, TCommand, TJobId, TShardKey>>()
            .AddAlias<IHostedService, WorkerPool<TService, TCommand, TJobId, TShardKey>>();

        services.AddSingleton<TService, TImplementation>();

        return services;
    }
}
