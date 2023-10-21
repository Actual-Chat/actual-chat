namespace ActualChat.Chat.UI.Blazor.Services;

public static class SpecialChat
{
    public static readonly Chat Unavailable = new(default, 0) {
        Title = "This chat is unavailable",
        Rules = AuthorRules.None(default),
    };
    public static readonly Chat Loading = new(default, -1) {
        Title = "Loading...",
        Rules = AuthorRules.None(default),
    };
    public static readonly Chat NoChatSelected = new(default, -2) {
        Title = "Select a chat",
        Rules = AuthorRules.None(default),
    };
}
