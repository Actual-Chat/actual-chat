namespace ActualChat.UI.Blazor.App.Components;

public enum ChatMessageReplacementKind
{
    None = 0,
    DateLine,
    NewMessagesLine,
    WelcomeBlock,
    SearchWelcomeBlock,
    Group,
    ConversationBlock,
    ConversationStart,
    ConversationEnd,
    SendingNewMessage,
}

public static class ChatMessageReplacementKindExt
{
    public static string GetKeySuffix(this ChatMessageReplacementKind replacementKind)
        => replacementKind switch {
            ChatMessageReplacementKind.None => "",
            ChatMessageReplacementKind.DateLine => "-date-line",
            ChatMessageReplacementKind.NewMessagesLine => "-new-messages",
            ChatMessageReplacementKind.WelcomeBlock => "-welcome-block",
            ChatMessageReplacementKind.Group => "-group",
            ChatMessageReplacementKind.SearchWelcomeBlock => "-search-welcome-block",
            ChatMessageReplacementKind.ConversationBlock => "-conversation-block",
            ChatMessageReplacementKind.ConversationStart => "-conversation", // We should use same suffix for conversation message and header
            ChatMessageReplacementKind.ConversationEnd => "-conversation-end",
            ChatMessageReplacementKind.SendingNewMessage => "-sending-new-msg",
            _ => throw new ArgumentOutOfRangeException(nameof(replacementKind), replacementKind, null),
        };
}
