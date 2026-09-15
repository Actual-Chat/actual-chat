namespace ActualChat.Chat;

public class ContentLinksBackend(IServiceProvider services) : IContentLinksBackend
{
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IPlacesBackend PlacesBackend { get; } = services.GetRequiredService<IPlacesBackend>();
    private KeyedFactory<IBackendChatMarkupHub, ChatId> ChatMarkupHubFactory { get; }
        = services.KeyedFactory<IBackendChatMarkupHub, ChatId>();

    public virtual async Task<ContentLinkInfo> GetContentInfo(ContentRef contentRef, CancellationToken cancellationToken)
    {
        switch (contentRef.ContentId) {
            case UserId userId: {
                var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
                if (account is null)
                    return ContentLinkInfo.RemovedOrUnknown(contentRef);

                return new ContentLinkInfo(
                    contentRef,
                    account.Avatar.Name,
                    account.Avatar.Picture,
                    account.Avatar.Bio);
            }
            case ChatId chatId: {
                var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
                if (chat is null)
                    return ContentLinkInfo.RemovedOrUnknown(contentRef);

                var title = chat.Title;
                if (chatId is PlaceChatId placeChatId) {
                    var place = await PlacesBackend.Get(placeChatId.PlaceId, cancellationToken).ConfigureAwait(false);
                    if (place is not null)
                        title += ", " + place.Title;
                }

                return new ContentLinkInfo(
                    contentRef,
                    title,
                    chat.Picture.ToPicture(),
                    chat.Description);
            }
            case ChatEntryId chatEntryId: {
                var textEntry = await ChatsBackend.GetEntry(chatEntryId, cancellationToken).ConfigureAwait(false);
                if (textEntry is null)
                    return ContentLinkInfo.RemovedOrUnknown(contentRef);

                var chatId = textEntry.AuthorId.ChatId;
                var author = await AuthorsBackend.Get(chatId,
                        textEntry.AuthorId,
                        RequestedAuthorKind.Full,
                        cancellationToken)
                    .ConfigureAwait(false);

                var title = author?.Avatar.Name ?? "Unknown";
                var chatInfo = await GetContentInfo(chatId.ContentRef, cancellationToken).ConfigureAwait(false);
                title += " in " + chatInfo.Title;
                var text = await GetText(textEntry, cancellationToken).ConfigureAwait(false);
                return new ContentLinkInfo(
                    contentRef,
                    title,
                    chatInfo.Picture,
                    text);
            }
            case AuthorId authorId: {
                var author = await AuthorsBackend.Get(authorId.ChatId, authorId, RequestedAuthorKind.Full, cancellationToken)
                    .ConfigureAwait(false);
                if (author is null)
                    return ContentLinkInfo.RemovedOrUnknown(contentRef);

                return new ContentLinkInfo(
                    contentRef,
                    author.Avatar.Name,
                    author.Avatar.Picture,
                    author.Avatar.Bio);
            }
            case PlaceId placeId: {
                var place = await PlacesBackend.Get(placeId, cancellationToken).ConfigureAwait(false);
                if (place is null)
                    return ContentLinkInfo.RemovedOrUnknown(contentRef);

                return new ContentLinkInfo(
                    contentRef,
                    place.Title,
                    place.Picture.ToPicture(),
                    place.Description);
            }
            default:
                throw StandardError.NotSupported(
                    contentRef.ContentId.GetType().GetName(), "Unsupported content link type.");
        }
    }

    // Private methods

    private async ValueTask<string> GetText(ChatEntry entry, CancellationToken cancellationToken)
    {
        var consumer = MarkupConsumer.Notification;
        var chatMarkupHub = ChatMarkupHubFactory[entry.ChatId];
        var markup = await chatMarkupHub.GetMarkup(entry, consumer, cancellationToken).ConfigureAwait(false);
        return markup.ToReadableText(consumer);
    }
}
