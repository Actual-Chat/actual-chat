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
