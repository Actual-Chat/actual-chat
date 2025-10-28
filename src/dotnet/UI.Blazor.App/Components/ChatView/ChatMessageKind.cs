namespace ActualChat.UI.Blazor.App.Components;

public enum ChatMessageKind
{
    None = 0, // TODO: Rename to Entry
    DateLine,
    NewMessagesLine,
    WelcomeBlock,
    SearchWelcomeBlock,
    Group,
    ConversationBlock,
    ConversationStart,
    ConversationEnd,
    Thread,
    SendingNewMessage,
}
