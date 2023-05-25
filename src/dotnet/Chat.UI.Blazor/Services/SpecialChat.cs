namespace ActualChat.Chat.UI.Blazor.Services;

public static class SpecialChat
{
    public static Chat Unavailable { get; } = new(default, 0) {
        Title = "This chat is unavailable",
        Rules = AuthorRules.None(default),
    };
    public static Chat Loading { get; } = new(default, -1) {
        Title = "Loading...",
        Rules = AuthorRules.None(default),
    };
    public static Chat NoChatSelected { get; } = new(default, -2) {
        Title = "Select a chat",
        Rules = AuthorRules.None(default),
    };
}
