using ActualChat.Users;

namespace ActualChat.Chat;

public class BackendChatMentionResolver : IChatMentionResolver
{
    private IAccountsBackend AccountsBackend { get; }
    private IChatAuthorsBackend ChatAuthorsBackend { get; }

    public Symbol ChatId { get; set; }

    public BackendChatMentionResolver(IServiceProvider services)
    {
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        ChatAuthorsBackend = services.GetRequiredService<IChatAuthorsBackend>();
    }

    ValueTask<Author?> IMentionResolver<Author>.Resolve(Mention mention, CancellationToken cancellationToken)
        => ResolveAuthor(mention, cancellationToken);
    public async ValueTask<Author?> ResolveAuthor(Mention mention, CancellationToken cancellationToken)
    {
        var targetId = mention.Id;
        if (targetId.OrdinalHasPrefix("u:", out var userId))
            return await AccountsBackend.GetUserAuthor(userId, cancellationToken).ConfigureAwait(false);
        if (!targetId.OrdinalHasPrefix("a:", out var authorId))
            authorId = targetId;
        return await ChatAuthorsBackend.Get(ChatId, authorId, true, cancellationToken).ConfigureAwait(false);
    }

    ValueTask<string?> IMentionResolver<string>.Resolve(Mention mention, CancellationToken cancellationToken)
        => ResolveName(mention, cancellationToken);
    public async ValueTask<string?> ResolveName(Mention mention, CancellationToken cancellationToken)
    {
        var author = await ResolveAuthor(mention, cancellationToken).ConfigureAwait(false);
        return author?.Name;
    }
}
