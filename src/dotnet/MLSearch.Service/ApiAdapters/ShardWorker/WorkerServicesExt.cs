
using Microsoft.Extensions.Hosting;

namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

internal static class WorkerServicesExt
{
    public static IServiceCollection AddShardWorkerAdapter(this IServiceCollection services)
    {
        services.AddSingleton(typeof(IShardIndexResolver<>), typeof(ShardIndexResolver<>));
        services.AddSingleton(typeof(IWorkerProcessFactory<,,,>), typeof(WorkerProcessFactory<,,,>));
        services.AddSingleton(typeof(IWorkerProcess<,,,>), typeof(WorkerProcess<,,,>));

        return services;
    }

    public static IServiceCollection AddShardWorker<TService, TImplementation, TCommand, TJobId, TShardKey>(
        this IServiceCollection services,
        DuplicateJobPolicy duplicateJobPolicy
    )
        where TService : class, IWorker<TCommand>
        where TImplementation : class, TService
        where TCommand : notnull, IHasId<TJobId>, IHasShardKey<TShardKey>
        where TJobId : notnull
        where TShardKey : notnull
    {
        services.AddSingleton(services
            => services.CreateInstanceWith<WorkerDispatcher<TService, TCommand, TJobId, TShardKey>>(
                ShardScheme.MLSearchBackend, duplicateJobPolicy))
            .AddAlias<IWorkerDispatcher<TCommand>, WorkerDispatcher<TService, TCommand, TJobId, TShardKey>>()
            .AddAlias<IHostedService, WorkerDispatcher<TService, TCommand, TJobId, TShardKey>>();

        services.AddSingleton<TService, TImplementation>();

        return services;
    }
}
