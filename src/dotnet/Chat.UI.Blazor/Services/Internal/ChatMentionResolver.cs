namespace ActualChat.Chat.UI.Blazor.Services.Internal;

internal class ChatMentionResolver(IServiceProvider services, ChatId chatId) : IChatMentionResolver
{
    private Session Session { get; } = services.Session();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();

    public ChatId ChatId { get; } = chatId;

    ValueTask<Author?> IMentionResolver<Author>.Resolve(MentionMarkup mention, CancellationToken cancellationToken)
        => ResolveAuthor(mention, cancellationToken);
    public async ValueTask<Author?> ResolveAuthor(MentionMarkup mention, CancellationToken cancellationToken)
    {
        if (!mention.Id.IsAuthor(out var authorId) || authorId.IsNone)
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
