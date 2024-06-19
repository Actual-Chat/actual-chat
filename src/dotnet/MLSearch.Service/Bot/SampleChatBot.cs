
using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.Media;

namespace ActualChat.MLSearch.Bot;

// Note: a simple stub implementation.
internal class SampleChatBot(ICommander commander, IMediaLinkPreviews mediaLinkPreviews, UrlMapper UrlMapper, IChatsBackend chats, ISearchEngine<ChatSlice> searchEngine)
    : IBotConversationHandler, IComputeService
{
    protected virtual string NotFoundMessage()
        => "Was not able to find anything";

    public async Task ExecuteAsync(
        IReadOnlyCollection<ChatEntry>? updatedDocuments,
        IReadOnlyCollection<ChatEntryId>? deletedDocuments,
        CancellationToken cancellationToken = default)
    {
        if (updatedDocuments == null) {
            return;
        }

        var lastUpdatedDocument = updatedDocuments.LastOrDefault();
        if (lastUpdatedDocument == null)
            return;

        var chatId = lastUpdatedDocument.ChatId;
        AuthorId botId = new(chatId, Constants.User.MLSearchBot.AuthorLocalId, AssumeValid.Option);
        if (lastUpdatedDocument.AuthorId == botId)
            return;

        // TODO: constrain with permissions
        var query = new SearchQuery() {
            MetadataFilters = [],
            FreeTextFilter = lastUpdatedDocument.Content,
        };
        var searchResult = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);
        var documents = searchResult.Documents;
        var foundDocument = documents.Count != 0 ? documents[0] : default;
        var chat = await chats.Get(chatId, cancellationToken).ConfigureAwait(false);

        var textEntryId = new TextEntryId(chatId, 0, AssumeValid.Option);
        long? entryId = null;
        if (!foundDocument.IsNone && !foundDocument.Document.Metadata.ChatEntries.IsEmpty){
            entryId = foundDocument.Document.Metadata.ChatEntries.First().LocalId;
        }
        /*
        ActualChat.Media.LinkPreview? linkPreview = null;
        LinkPreviewMode? linkPreviewMode = LinkPreviewMode.None;
        if (!foundDocument.Document.Metadata.ChatEntries.IsEmpty){
            entryId = foundDocument.Document.Metadata.ChatEntries.First().LocalId;
            linkPreview = await mediaLinkPreviews.GetForEntry(foundDocument.Document.Metadata.ChatId, foundDocument.Document.Metadata.ChatEntries.First().Id, cancellationToken).ConfigureAwait(false);
            linkPreviewMode = LinkPreviewMode.Full;
        }
        */
        var link = Links.Chat(foundDocument.Document.Metadata.ChatId, entryId).ToAbsolute(UrlMapper);
        // TODO: How to add this link as a preview?
        var text = foundDocument.IsNone
            ? NotFoundMessage()
            // : foundDocument.Document.Text
            : "I've found something. Take a look here: " + link;
            

        var upsertCommand = new ChatsBackend_ChangeEntry(
            textEntryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = botId,
                Content = text,
            }));
        await commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
