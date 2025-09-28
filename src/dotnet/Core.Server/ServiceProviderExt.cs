using ActualChat.Queues;
using Microsoft.Extensions.Hosting;

namespace ActualChat;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BackendServiceDefs BackendServiceDefs(this IServiceProvider services)
        => services.GetRequiredService<BackendServiceDefs>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IHostApplicationLifetime HostLifetime(this IServiceProvider services)
        => services.GetRequiredService<IHostApplicationLifetime>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IHostApplicationLifetime? HostLifetimeIfExist(this IServiceProvider services)
        => services.GetService<IHostApplicationLifetime>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IMeshLocks<TContext> MeshLocks<TContext>(this IServiceProvider services)
        => services.GetRequiredService<IMeshLocks<TContext>>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MeshWatcher MeshWatcher(this IServiceProvider services)
        => services.GetRequiredService<MeshWatcher>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ShardBrokers ShardBrokers(this IServiceProvider services)
        => services.GetRequiredService<ShardBrokers>();
    public static ShardBroker ShardBroker<TBackend>(this IServiceProvider services)
        => services.GetRequiredService<ShardBrokers>()[typeof(TBackend)];
    public static ShardBroker ShardBroker(this IServiceProvider services, Type backendServiceType)
        => services.GetRequiredService<ShardBrokers>()[backendServiceType];
    public static ShardBroker ShardBroker(this IServiceProvider services, ShardScheme shardScheme)
        => services.GetRequiredService<ShardBrokers>()[shardScheme];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IBlobStorages BlobStorages(this IServiceProvider services)
        => services.GetRequiredService<IBlobStorages>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IQueues Queues(this IServiceProvider services)
        => services.GetRequiredService<IQueues>();
}
