using ActualChat.Users;

namespace ActualChat.Chat;

public class BackendChatMentionResolver : IChatMentionResolver
{
    private IAuthorsBackend AuthorsBackend { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IChatsBackend ChatsBackend { get; }
    private IPlacesBackend PlacesBackend { get; }

    public ChatId ChatId { get; }

    public BackendChatMentionResolver(IServiceProvider services, ChatId chatId)
    {
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        ChatsBackend = services.GetRequiredService<IChatsBackend>();
        PlacesBackend = services.GetRequiredService<IPlacesBackend>();
        ChatId = chatId;
    }

    ValueTask<Author?> IMentionResolver<Author>.Resolve(MentionMarkup mention, CancellationToken cancellationToken)
        => ResolveAuthor(mention, cancellationToken);
    public async ValueTask<Author?> ResolveAuthor(MentionMarkup mention, CancellationToken cancellationToken)
    {
        if (mention.Id.Target is not AuthorId authorId)
            return null;

        return await AuthorsBackend.Get(ChatId, authorId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
    }

    ValueTask<string?> IMentionResolver<string>.Resolve(MentionMarkup mention, CancellationToken cancellationToken)
        => ResolveName(mention, cancellationToken);
    public async ValueTask<string?> ResolveName(MentionMarkup mention, CancellationToken cancellationToken)
    {
        var enriched = await Enrich(mention, cancellationToken).ConfigureAwait(false);
        return enriched.Name.NullIfEmpty();
    }

    public async ValueTask<MentionMarkup> Enrich(MentionMarkup mention, CancellationToken cancellationToken)
    {
        switch (mention) {
        case AuthorMention am: {
            var author = await AuthorsBackend.Get(ChatId, am.AuthorId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
            var name = author?.Avatar.Name ?? am.Name;
            return new AuthorMention(am.Id, name) { Author = author };
        }
        case UserMention um: {
            var accountTask = AccountsBackend.Get(um.UserId, cancellationToken);
            var authorTask = AuthorsBackend.GetByUserId(ChatId, um.UserId, RequestedAuthorKind.Default, cancellationToken);
            var account = await accountTask.ConfigureAwait(false);
            var author = await authorTask.ConfigureAwait(false);
            var name = account?.Avatar.Name ?? um.Name;
            return new UserMention(um.Id, name) {
                Account = account,
                IsChatMember = author is not null,
            };
        }
        case ChatMention cm: {
            var chat = await ChatsBackend.Get(cm.ChatId, cancellationToken).ConfigureAwait(false);
            var title = chat?.Title.NullIfEmpty() ?? cm.Name;
            var chatPlaceId = cm.ChatId is PlaceChatId pc ? pc.PlaceId : null;
            var name = await FormatChatName(title, chatPlaceId, cancellationToken).ConfigureAwait(false);
            return new ChatMention(cm.Id, name) { Chat = chat };
        }
        case PlaceMention pm: {
            var place = await PlacesBackend.Get(pm.PlaceId, cancellationToken).ConfigureAwait(false);
            var name = place?.Title.NullIfEmpty() ?? pm.Name;
            return new PlaceMention(pm.Id, name) { Place = place };
        }
        case EmojiMention em: {
            var emoji = Emojis.ById.GetValueOrDefault(em.EmojiRef.Text);
            return emoji is null
                ? em
                : new EmojiMention(em.Id, em.Name) { Glyph = emoji.Symbol };
        }
        default:
            return mention;
        }
    }

    // A cross-place chat mention renders as "Place › Chat Title"; same-place and placeless
    // chats render as just the title.
    private async ValueTask<string> FormatChatName(string title, PlaceId? chatPlaceId, CancellationToken cancellationToken)
    {
        if (chatPlaceId is null)
            return title;
        var hostPlaceId = (ChatId as PlaceChatId)?.PlaceId;
        if (hostPlaceId == chatPlaceId)
            return title;
        var place = await PlacesBackend.Get(chatPlaceId, cancellationToken).ConfigureAwait(false);
        var placeTitle = place?.Title.NullIfEmpty();
        return placeTitle is null ? title : $"{placeTitle} › {title}";
    }
}
