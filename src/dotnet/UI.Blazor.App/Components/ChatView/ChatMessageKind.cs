namespace ActualChat.UI.Blazor.App.Components;

public enum ChatMessageKind
{
    None = 0, // TODO: Rename to Entry
    DateLine,
    NewMessagesLine,
    WelcomeBlock,
    Group,
    ConversationBlock,
    ConversationStart,
    ConversationEnd,
    LiveConversationHeader,
    Thread,
    SendingNewMessage,
    AudioRecordingMessage,
}

public static class ChatMessageKindExt
{
    // Placeholders stand in for entries the server hasn't accepted yet - an optimistic send, the
    // audio-recording skeleton. Their lids are synthetic and sit at or beyond the chat's end, so
    // anything deciding "how far has this been read" must skip them.
    public static bool IsPlaceholder(this ChatMessageKind kind)
        => kind is ChatMessageKind.SendingNewMessage or ChatMessageKind.AudioRecordingMessage;
}
