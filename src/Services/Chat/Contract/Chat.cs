using System;
using System.Collections.Immutable;
using System.Reactive;
using Stl.Fusion.Authentication;

namespace ActualChat.Chat
{
    public record Chat(string Id, ChatKind Kind)
    {
        public interface IChatCommand : ISessionCommand
        {
            string ChatId { get; }
        }
        public interface IChatCommand<TResult> : ISessionCommand<TResult>, IChatCommand { }

        public record PostCommand(Session Session, string ChatId, string Text) : IChatCommand<ChatMessage> {
            public PostCommand() : this(Session.Null, "", "") { }
        }
        public record DeleteCommand(Session Session, string ChatId, string MessageId) : IChatCommand<Unit> {
            public DeleteCommand() : this(Session.Null, "", "") { }
        }

        public static string GetPublicChatId(string chatId)
        {
            if (string.IsNullOrEmpty(chatId))
                throw new ArgumentOutOfRangeException(nameof(chatId));
            return $"public/{chatId}";
        }

        public static string GetP2PChatId(long user1Id, long user2Id)
        {
            if (user1Id == user2Id)
                throw new ArgumentOutOfRangeException(nameof(user2Id));
            var lowId = Math.Min(user1Id, user2Id);
            var highId = Math.Max(user1Id, user2Id);
            return $"p2p/{lowId}/{highId}";
        }
        
        public static string GetSecretChatId(long user1Id, long user2Id)
        {
            if (user1Id == user2Id)
                throw new ArgumentOutOfRangeException(nameof(user2Id));
            var lowId = Math.Min(user1Id, user2Id);
            var highId = Math.Max(user1Id, user2Id);
            return $"secret/{lowId}/{highId}";
        }

        public ImmutableHashSet<long> OwnerIds { get; init; } = ImmutableHashSet<long>.Empty;
        public ImmutableHashSet<long> ParticipantIds { get; init; } = ImmutableHashSet<long>.Empty;
        public bool IsPublic { get; init; }

        public Chat() : this("", ChatKind.Unknown) { }
    }
}
