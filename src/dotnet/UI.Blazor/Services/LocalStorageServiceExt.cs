using Blazored.LocalStorage;

namespace ActualChat.UI.Blazor.Services;

public static class LocalStorageServiceExt
{
    public static ValueTask<string> GetActiveChatId(this ILocalStorageService localStorage)
        => localStorage.GetItemAsStringAsync("Chat.ActiveChatId");

    public static ValueTask SetActiveChatId(this ILocalStorageService localStorage, string value)
        => localStorage.SetItemAsStringAsync("Chat.ActiveChatId", value);
}
