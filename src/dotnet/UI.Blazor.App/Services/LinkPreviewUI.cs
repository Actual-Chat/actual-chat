namespace ActualChat.UI.Blazor.App.Services;

public class LinkPreviewUI(ChatUIHub hub) : ScopedServiceBase<ChatUIHub>(hub), IComputeService
{
    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;
    private IPlaces Places => Hub.Places;

    [ComputeMethod]
    public virtual async Task<bool> IsRenderedAsLocal(string url, CancellationToken cancellationToken)
    {
        var localLinkInfo = await TryGetLocal(url, cancellationToken).ConfigureAwait(false);
        return localLinkInfo?.CanRender == true;
    }

    [ComputeMethod]
    public virtual async Task<LocalLinkInfo?> TryGetLocal(string url, CancellationToken cancellationToken)
    {
        if (LocalUrl.FromAbsolute(url, UrlMapper) is not { } localUrlOpt)
            return null;

        var localLinkInfo = await GetLocal(localUrlOpt, cancellationToken).ConfigureAwait(false);
        return localLinkInfo;
    }

    [ComputeMethod]
    public virtual async Task<LocalLinkInfo> GetLocal(LocalUrl localUrl, CancellationToken cancellationToken)
    {
        var localLinkModel = new LocalLinkInfo(localUrl);
        if (!localUrl.IsChatCompat(out var chatId, out var entryLid))
            return localLinkModel;

        // Chat message link
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is not null) {
            localLinkModel = localLinkModel with { Chat = chat };
            if (entryLid > 0) {
                var textEntryId = new TextEntryId(chatId, entryLid, AssumeValid.Option);
                var entry = await Chats.GetEntry(Session, textEntryId, cancellationToken).ConfigureAwait(false);
                localLinkModel = localLinkModel with { Entry = entry };
                if (entry is not null) {
                    var authorId = entry.AuthorId;
                    var author = await Authors.Get(Session, entry.ChatId, authorId, cancellationToken).ConfigureAwait(false);
                    localLinkModel = localLinkModel with { Author = author };
                }
            }
        }
        var placeId = chatId.PlaceChatId.PlaceId;
        if (!placeId.IsNone) {
            var place = await Places.Get(Session, placeId, cancellationToken).ConfigureAwait(false);
            localLinkModel = localLinkModel with { Place = place };
        }
        return localLinkModel;
    }
}
