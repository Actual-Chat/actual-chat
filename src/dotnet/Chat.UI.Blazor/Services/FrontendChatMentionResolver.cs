using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class FrontendChatMentionResolver : IChatMentionResolver
{
    private Session Session { get; }
    private IAccounts Accounts { get; }
    private IAuthors Authors { get; }

    public Symbol ChatId { get; set; }

    public FrontendChatMentionResolver(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();
        Authors = services.GetRequiredService<IAuthors>();
    }

    ValueTask<Author?> IMentionResolver<Author>.Resolve(MentionMarkup mention, CancellationToken cancellationToken)
        => ResolveAuthor(mention, cancellationToken);
    public async ValueTask<Author?> ResolveAuthor(MentionMarkup mention, CancellationToken cancellationToken)
    {
        var targetId = mention.Id;
        if (targetId.OrdinalHasPrefix("u:", out var userId))
            throw StandardError.NotSupported("User mentions aren't supported yet.");
        if (!targetId.OrdinalHasPrefix("a:", out var authorId))
            authorId = targetId;
        return await Authors.Get(Session, ChatId, authorId, cancellationToken).ConfigureAwait(false);
    }

    ValueTask<string?> IMentionResolver<string>.Resolve(MentionMarkup mention, CancellationToken cancellationToken)
        => ResolveName(mention, cancellationToken);
    public async ValueTask<string?> ResolveName(MentionMarkup mention, CancellationToken cancellationToken)
    {
        var author = await ResolveAuthor(mention, cancellationToken).ConfigureAwait(false);
        return author?.Avatar.Name;
    }
}
