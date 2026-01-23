using ActualChat.Kvas;

namespace ActualChat;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AccountSettings AccountSettings(this IServiceProvider services, Session session)
        => new(services.ServerKvas(), session);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AccountSettings AccountSettings(this IServiceProvider services)
        => services.GetRequiredService<AccountSettings>();
}
