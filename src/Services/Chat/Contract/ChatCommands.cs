using Stl.Fusion.Authentication;

namespace ActualChat.Chat
{
    public static partial class ChatCommands
    {
        // Base type for any chat command

        public abstract record Base(Session Session, string ChatId)
            : ISessionCommand
        { }

        public abstract record Base<TResult>(Session Session, string ChatId)
            : Base(Session, ChatId), ISessionCommand<TResult>
        { }

        // Actual commands

        public record AddText(Session Session, string ChatId, string Text)
            : Base<ChatEntry>(Session, ChatId) {
            public AddText() : this(Session.Null, "", "") { }
        }
    }
}
