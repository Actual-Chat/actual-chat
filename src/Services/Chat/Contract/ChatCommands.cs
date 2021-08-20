using Stl.Fusion.Authentication;

namespace ActualChat.Chat
{
    public static partial class ChatCommands
    {
        // Base type for any chat command

        public abstract record Any(Session Session, string ChatId)
            : ISessionCommand
        { }

        public abstract record Any<TResult>(Session Session, string ChatId)
            : Any(Session, ChatId), ISessionCommand<TResult>
        { }

        // Actual commands

        public record PostText(Session Session, string ChatId, string Text)
            : Any<ChatEntry>(Session, ChatId) {
            public PostText() : this(Session.Null, "", "") { }
        }
    }
}
