namespace ActualChat.Chat;

public static partial class ChatCommands
{
    // ToDo: separate user and server-side commands
    // ToDo: remove base types in the file

    // Base type for any chat command
    public abstract record Cmd(Session Session, ChatId ChatId)
        : ISessionCommand
    { }

    public abstract record Cmd<TResult>(Session Session, ChatId ChatId)
        : Cmd(Session, ChatId), ISessionCommand<TResult>
    { }

    // User commands

    public record CreateChat(Session Session, string Title) : Cmd<Chat>(Session, "") {
        public CreateChat() : this(Session.Null, "") { }
    }

    public record PostMessage(Session Session, ChatId ChatId, string Text)
        : Cmd<ChatEntry>(Session, ChatId) {
        public PostMessage() : this(Session.Null, "", "") { }
    }

    // Server-side commands

    /// <summary>
    /// For creating chat entries from server (for example audio service)
    /// </summary>
    public record CreateEntry(ChatEntry Entry) : ServerSideCommandBase<ChatEntry> {

        public CreateEntry() : this((ChatEntry) null!) { }
    }

    public record UpdateEntry(ChatEntry Entry) : ServerSideCommandBase<ChatEntry> {

        public UpdateEntry() : this((ChatEntry) null!) { }
    }
}
