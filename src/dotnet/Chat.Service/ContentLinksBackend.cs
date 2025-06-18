using ActualChat.Media;
using ActualChat.Users;

namespace ActualChat.Chat;

public class ContentLinksBackend(IServiceProvider services) : IContentLinksBackend
{
    [field: AllowNull, MaybeNull]
    private IAccountsBackend AccountsBackend => field ??= services.GetRequiredService<IAccountsBackend>();
    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= services.GetRequiredService<IChatsBackend>();
    [field: AllowNull, MaybeNull]
    private IAuthorsBackend AuthorsBackend => field ??= services.GetRequiredService<IAuthorsBackend>();
    [field: AllowNull, MaybeNull]
    private IPlacesBackend PlacesBackend => field ??= services.GetRequiredService<IPlacesBackend>();

    public virtual async Task<ContentLinkInfo> GetContentInfo(ContentId contentId, CancellationToken cancellationToken)
    {
        var kind = contentId.Kind;
        var sid = contentId.Id.Value;
        switch (kind) {
            case ContentKind.User: {
                var userId = UserId.Parse(sid);
                var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
                if (account is null)
                    return ContentLinkInfo.RemovedOrUnknown(contentId);

                return new ContentLinkInfo(
                    contentId,
                    account.Avatar.Name,
                    account.Avatar.Picture,
                    account.Avatar.Bio);
            }
            case ContentKind.Chat: {
                var chatId = ChatId.Parse(sid);
                var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
                if (chat is null)
                    return ContentLinkInfo.RemovedOrUnknown(contentId);

                var title = chat.Title;
                if (chatId is PlaceChatId placeChatId) {
                    var place = await PlacesBackend.Get(placeChatId.PlaceId, cancellationToken).ConfigureAwait(false);
                    if (place is not null)
                        title += ", " + place.Title;
                }
                return new ContentLinkInfo(
                    contentId,
                    title,
                    chat.Picture.ToPicture(),
                    chat.Description);
            }
            case ContentKind.Author: {
                var authorId = AuthorId.Parse(sid);
                var author = await AuthorsBackend.Get(authorId.ChatId, authorId, RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
                if (author is null)
                    return ContentLinkInfo.RemovedOrUnknown(contentId);

                return new ContentLinkInfo(
                    contentId,
                    author.Avatar.Name,
                    author.Avatar.Picture,
                    author.Avatar.Bio);
            }
            case ContentKind.Place: {
                var placeId = PlaceId.Parse(sid);
                var place = await PlacesBackend.Get(placeId, cancellationToken).ConfigureAwait(false);
                if (place is null)
                    return ContentLinkInfo.RemovedOrUnknown(contentId);

                return new ContentLinkInfo(
                    contentId,
                    place.Title,
                    place.Picture.ToPicture(),
                    place.Description);
            }
            default:
                throw StandardError.NotSupported(kind.ToString(), "Invalid content id kind.");
        }
    }
}
