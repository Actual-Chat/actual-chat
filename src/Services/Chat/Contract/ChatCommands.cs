using Stl.Fusion.Authentication;

namespace ActualChat.Chat
{
    public static partial class ChatCommands
    {
        // Base type for any chat command

        public abstract record Cmd(Session Session, string ChatId)
            : ISessionCommand
        { }

        public abstract record Cmd<TResult>(Session Session, string ChatId)
            : Cmd(Session, ChatId), ISessionCommand<TResult>
        { }

        // Actual commands

        public record Create(Session Session, string Title) : Cmd<Chat>(Session, "") {
            public Create() : this(Session.Null, "") { }
        }

        public record Post(Session Session, string ChatId, string Text)
            : Cmd<ChatEntry>(Session, ChatId) {
            public Post() : this(Session.Null, "", "") { }
        }
    }
}
