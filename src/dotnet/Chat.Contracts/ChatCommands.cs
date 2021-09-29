using Stl.CommandR;
using Stl.CommandR.Commands;
using Stl.Fusion.Authentication;

namespace ActualChat.Chat
{
    public static partial class ChatCommands
    {
        // Base type for any chat command

        public abstract record Cmd(Session Session, ChatId ChatId)
            : ISessionCommand
        { }

        public abstract record Cmd<TResult>(Session Session, ChatId ChatId)
            : Cmd(Session, ChatId), ISessionCommand<TResult>
        { }

        // User commands

        public record Create(Session Session, string Title) : Cmd<Chat>(Session, "") {
            public Create() : this(Session.Null, "") { }
        }

        public record Post(Session Session, ChatId ChatId, string Text)
            : Cmd<ChatEntry>(Session, ChatId) {
            public Post() : this(Session.Null, "", "") { }
        }

        // Server-side commands

        public record CreateEntry(ChatEntry Entry) : ServerSideCommandBase<ChatEntry> {

            public CreateEntry() : this((ChatEntry) null!) { }
        }

        public record UpdateEntry(ChatEntry Entry) : ServerSideCommandBase<ChatEntry> {

            public UpdateEntry() : this((ChatEntry) null!) { }
        }
    }
}
