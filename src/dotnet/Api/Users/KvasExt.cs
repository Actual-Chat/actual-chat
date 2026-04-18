using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// KVAS (key-value async store) extension methods for user settings.
/// </summary>
public static class KvasExt
{
    public static KvasAccessor<LocalAppSettings> LocalAppSettings(this IKvas kvas)
        => kvas.AccessorFor<LocalAppSettings>();
}
