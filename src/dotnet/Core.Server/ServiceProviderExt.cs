using ActualChat.Mesh;
using ActualChat.Queues;
using Microsoft.Extensions.Hosting;

namespace ActualChat;

public static class ServiceProviderExt
{
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
    public static IBlobStorages BlobStorages(this IServiceProvider services)
        => services.GetRequiredService<IBlobStorages>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IQueues Queues(this IServiceProvider services)
        => services.GetRequiredService<IQueues>();
}
