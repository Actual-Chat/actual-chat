using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ChatUIHub ChatUIHub(this IServiceProvider services)
        => services.GetRequiredService<ChatUIHub>();
}
