namespace ActualChat.UI.Blazor.Services;

public static class LocalSettingsExt
{
    public static ValueTask<string?> GetActiveChatId(this LocalSettings localSettings)
        => localSettings.Get("Chat/ActiveChatId");

    public static void SetActiveChatId(this LocalSettings localSettings, string value)
        => localSettings.Set("Chat/ActiveChatId", value);
}
