namespace ActualChat.Chat.UI.Blazor.Components;

public enum ChatMessageReplacementKind
{
    None = 0,
    DateLine,
    NewMessagesLine,
    WelcomeBlock,
}

public static class ChatMessageReplacementKindExt
{
    public static string GetKeySuffix(this ChatMessageReplacementKind replacementKind)
        => replacementKind switch {
            ChatMessageReplacementKind.None => "",
            ChatMessageReplacementKind.DateLine => "-date-line",
            ChatMessageReplacementKind.NewMessagesLine => "-new-messages",
            ChatMessageReplacementKind.WelcomeBlock => "-welcome-block",
            _ => throw new ArgumentOutOfRangeException(nameof(replacementKind), replacementKind, null),
        };
}
