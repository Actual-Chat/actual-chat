using ActualChat.Users;

namespace ActualChat.Chat;

public class BackendChatMentionResolver : IChatMentionResolver
{
    private IAccountsBackend AccountsBackend { get; }
    private IAuthorsBackend AuthorsBackend { get; }

    public ChatId ChatId { get; set; }

    public BackendChatMentionResolver(IServiceProvider services)
    {
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
    }

    ValueTask<Author?> IMentionResolver<Author>.Resolve(MentionMarkup mention, CancellationToken cancellationToken)
        => ResolveAuthor(mention, cancellationToken);
    public async ValueTask<Author?> ResolveAuthor(MentionMarkup mention, CancellationToken cancellationToken)
    {
        if (!mention.Id.OrdinalHasPrefix("a:", out var sAuthorId))
            return null;

        var authorId = new AuthorId(sAuthorId, ParseOptions.OrDefault);
        if (authorId.IsEmpty)
            return null;

        return await AuthorsBackend.Get(ChatId, authorId, cancellationToken).ConfigureAwait(false);
    }

    ValueTask<string?> IMentionResolver<string>.Resolve(MentionMarkup mention, CancellationToken cancellationToken)
        => ResolveName(mention, cancellationToken);
    public async ValueTask<string?> ResolveName(MentionMarkup mention, CancellationToken cancellationToken)
    {
        var author = await ResolveAuthor(mention, cancellationToken).ConfigureAwait(false);
        return author?.Avatar.Name;
    }
}
