namespace ActualChat.Chat;

// ReSharper disable once PossibleInterfaceMemberAmbiguity
public interface IChatMentionResolver : IMentionResolver<Author>, IMentionResolver<string>
{
    ChatId ChatId { get; set; }

    ValueTask<Author?> ResolveAuthor(MentionMarkup mention, CancellationToken cancellationToken);
    ValueTask<string?> ResolveName(MentionMarkup mention, CancellationToken cancellationToken);
}
