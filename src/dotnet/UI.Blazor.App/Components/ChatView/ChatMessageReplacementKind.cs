namespace ActualChat.UI.Blazor.App.Components;

public enum ChatMessageReplacementKind
{
    None = 0,
    DateLine,
    NewMessagesLine,
    WelcomeBlock,
    SearchWelcomeBlock,
}

public static class ChatMessageReplacementKindExt
{
    public static string GetKeySuffix(this ChatMessageReplacementKind replacementKind)
        => replacementKind switch {
            ChatMessageReplacementKind.None => "",
            ChatMessageReplacementKind.DateLine => "-date-line",
            ChatMessageReplacementKind.NewMessagesLine => "-new-messages",
            ChatMessageReplacementKind.WelcomeBlock => "-welcome-block",
            ChatMessageReplacementKind.SearchWelcomeBlock => "-search-welcome-block",
            _ => throw new ArgumentOutOfRangeException(nameof(replacementKind), replacementKind, null),
        };
}
