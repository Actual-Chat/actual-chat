
using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;

namespace ActualChat.MLSearch.Bot;

// Note: a simple stub implementation.
internal class SampleChatBot(ICommander commander, IChatsBackend chats, ISearchEngine<ChatSlice> searchEngine)
    : IBotConversationHandler, IComputeService
{
    protected virtual string NotFoundMessage()
        => "Was not able to find anything";

    public async Task ExecuteAsync(IEnumerable<ChatEntry>? updatedDocuments, IEnumerable<ChatEntryId>? deletedDocuments, CancellationToken cancellationToken) {

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

        var text = foundDocument.IsNone
            ? NotFoundMessage()
            : foundDocument.Document.Text;

        var textEntryId = new TextEntryId(chatId, 0, AssumeValid.Option);
            var upsertCommand = new ChatsBackend_ChangeEntry(
                textEntryId,
                null,
                Change.Create(new ChatEntryDiff {
                    AuthorId = botId,
                    Content = text,
                    RepliedEntryLocalId = default,
                }));
            await commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
