namespace ActualChat.Chat.UI.Blazor.Services;

public static class ServiceProviderExt
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ChatHub ChatHub(this IServiceProvider services)
        => services.GetRequiredService<ChatHub>();
}
