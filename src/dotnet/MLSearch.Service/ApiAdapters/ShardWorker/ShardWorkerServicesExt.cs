
using Microsoft.Extensions.Hosting;

namespace ActualChat.MLSearch.ApiAdapters.ShardWorker;

internal static class ShardWorkerServicesExt
{
    public static IServiceCollection AddShardWorkerAdapter(this IServiceCollection services)
    {
        services.AddSingleton(typeof(IShardIndexResolver<>), typeof(ShardIndexResolver<>));
        services.AddSingleton(typeof(IShardWorkerProcessFactory<,,,>), typeof(ShardWorkerProcessFactory<,,,>));
        services.AddSingleton(typeof(IShardWorkerProcess<,,,>), typeof(ShardWorkerProcess<,,,>));

        return services;
    }

    public static IServiceCollection AddShardWorker<TService, TImplementation, TCommand, TJobId, TShardKey>(
        this IServiceCollection services,
        DuplicateJobPolicy duplicateJobPolicy
    )
        where TService : class, IWorker<TCommand>
        where TImplementation : class, TService
        where TCommand : IHasId<TJobId>, IHasShardKey<TShardKey>
    {
        services.AddSingleton(services
            => services.CreateInstanceWith<ShardCommandWorker<TService, TCommand, TJobId, TShardKey>>(
                ShardScheme.MLSearchBackend, duplicateJobPolicy))
            .AddAlias<IShardCommandDispatcher<TCommand>, ShardCommandWorker<TService, TCommand, TJobId, TShardKey>>()
            .AddAlias<IHostedService, ShardCommandWorker<TService, TCommand, TJobId, TShardKey>>();

        services.AddSingleton<TService, TImplementation>();

        return services;
    }
}
