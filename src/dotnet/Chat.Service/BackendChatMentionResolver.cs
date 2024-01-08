namespace ActualChat.Chat;

public class BackendChatMentionResolver : IChatMentionResolver
{
    private IAuthorsBackend AuthorsBackend { get; }

    public ChatId ChatId { get; }

    public BackendChatMentionResolver(IServiceProvider services, ChatId chatId)
    {
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        ChatId = chatId;
    }

    ValueTask<Author?> IMentionResolver<Author>.Resolve(MentionMarkup mention, CancellationToken cancellationToken)
        => ResolveAuthor(mention, cancellationToken);
    public async ValueTask<Author?> ResolveAuthor(MentionMarkup mention, CancellationToken cancellationToken)
    {
        if (!mention.Id.IsAuthor(out var authorId) || authorId.IsNone)
            return null;

        return await AuthorsBackend.Get(ChatId, authorId, AuthorsBackend_GetAuthorOption.Full, cancellationToken).ConfigureAwait(false);
    }

    ValueTask<string?> IMentionResolver<string>.Resolve(MentionMarkup mention, CancellationToken cancellationToken)
        => ResolveName(mention, cancellationToken);
    public async ValueTask<string?> ResolveName(MentionMarkup mention, CancellationToken cancellationToken)
    {
        var author = await ResolveAuthor(mention, cancellationToken).ConfigureAwait(false);
        return author?.Avatar.Name;
    }
}
