using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatMentionResolver : IChatMentionResolver
{
    private Session Session { get; }
    private IAccounts Accounts { get; }
    private IAuthors Authors { get; }

    public ChatId ChatId { get; set; }

    public ChatMentionResolver(IServiceProvider services, ChatId chatId)
    {
        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();
        Authors = services.GetRequiredService<IAuthors>();
        ChatId = chatId;
    }

    ValueTask<Author?> IMentionResolver<Author>.Resolve(MentionMarkup mention, CancellationToken cancellationToken)
        => ResolveAuthor(mention, cancellationToken);
    public async ValueTask<Author?> ResolveAuthor(MentionMarkup mention, CancellationToken cancellationToken)
    {
        if (!mention.Id.OrdinalHasPrefix("a:", out var sAuthorId))
            return null;

        var authorId = new AuthorId(sAuthorId, ParseOrNone.Option);
        if (authorId.IsNone)
            return null;

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
