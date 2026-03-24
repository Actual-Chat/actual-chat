using ActualChat.Kvas;

namespace ActualChat;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LocalSettings LocalSettings(this IServiceProvider services)
        => services.GetRequiredService<LocalSettings>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IUserSettings UserSettings(this IServiceProvider services)
        => services.GetRequiredService<IUserSettings>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static UserSettingsUI UserSettingsUI(this IServiceProvider services, Session session)
        => new(services, session);
}
