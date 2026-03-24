using ActualChat.Kvas;

namespace ActualChat;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LocalSettings LocalSettings(this IServiceProvider services)
        => services.GetRequiredService<LocalSettings>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IAccountSettings AccountSettings(this IServiceProvider services)
        => services.GetRequiredService<IAccountSettings>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AccountSettingsUI AccountSettingsUI(this IServiceProvider services, Session session)
        => new(services, session);
}
