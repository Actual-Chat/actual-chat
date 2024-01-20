namespace ActualChat.Mesh;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MeshNode MeshNode(this IServiceProvider services)
        => services.GetRequiredService<MeshNode>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IMeshLocks<TContext> MeshLocks<TContext>(this IServiceProvider services)
        => services.GetRequiredService<IMeshLocks<TContext>>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MeshWatcher MeshWatcher(this IServiceProvider services)
        => services.GetRequiredService<MeshWatcher>();
}
