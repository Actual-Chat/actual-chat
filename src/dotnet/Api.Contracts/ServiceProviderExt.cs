using ActualChat.Kvas;

namespace ActualChat;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LocalSettings LocalSettings(this IServiceProvider services)
        => services.GetRequiredService<LocalSettings>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ScopedAccountSettings AccountSettings(this IServiceProvider services, Session session)
        => new(services, session);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ScopedAccountSettings AccountSettings(this IServiceProvider services)
        => services.GetRequiredService<ScopedAccountSettings>();
}
