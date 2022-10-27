namespace ActualChat.Chat;

// ReSharper disable once PossibleInterfaceMemberAmbiguity
public interface IChatMentionResolver : IMentionResolver<ChatAuthor>, IMentionResolver<string>
{
    Symbol ChatId { get; set; }

    ValueTask<ChatAuthor?> ResolveAuthor(MentionMarkup mention, CancellationToken cancellationToken);
    ValueTask<string?> ResolveName(MentionMarkup mention, CancellationToken cancellationToken);
}
