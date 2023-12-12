using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ChatUIHub ChatUIHub(this IServiceProvider services)
        => services.GetRequiredService<ChatUIHub>();
}
