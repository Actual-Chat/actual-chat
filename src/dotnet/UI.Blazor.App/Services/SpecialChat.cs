namespace ActualChat.UI.Blazor.App.Services;

public static class SpecialChat
{
    public static readonly Chat.Chat Unavailable = new(null!, 0) {
        Title = "This chat is unavailable",
        Rules = AuthorRules.None(null!),
    };
    public static readonly Chat.Chat Loading = new(null!, -1) {
        Title = "Loading...",
        Rules = AuthorRules.None(null!),
    };
    public static readonly Chat.Chat NoChatSelected = new(null!, -2) {
        Title = "Select a chat",
        Rules = AuthorRules.None(null!),
    };
}
