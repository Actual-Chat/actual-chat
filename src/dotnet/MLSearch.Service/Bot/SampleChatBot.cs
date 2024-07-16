
using ActualChat.Chat;
using ActualChat.MLSearch.Documents;
using ActualChat.MLSearch.Engine;
using ActualChat.Media;

namespace ActualChat.MLSearch.Bot;

// Note: a simple stub implementation.
internal class SampleChatBot(ICommander commander, UrlMapper urlMapper, IChatsBackend chats, ISearchEngine<ChatSlice> searchEngine)
    : IBotConversationHandler, IComputeService
{
    protected virtual string NotFoundMessage()
        => "Was not able to find anything relevant";

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
            Filters = [
                new ChatFilter() {
                    PublicChatInclusion = InclusionMode.IncludeStrictly,
                    SearchBotChatInclusion = InclusionMode.Exclude,
                },
                new FreeTextFilter<ChatSlice>(lastUpdatedDocument.Content),
            ],
        };
        var searchResult = await searchEngine.Find(query, cancellationToken).ConfigureAwait(false);
        var documents = searchResult.Documents;
        var foundDocument = documents.Count != 0 ? documents[0] : default;
        var chat = await chats.Get(chatId, cancellationToken).ConfigureAwait(false);
        var textEntryId = new TextEntryId(chatId, 0, AssumeValid.Option);

        string? text;
        ChatEntryId? forwardedFrom = null;
        if (!foundDocument.IsNone && !foundDocument.Document.Metadata.ChatEntries.IsEmpty) {
            // Note: (Andrew Kurochkin) I've tried to use existing options:
            // link preview, forwarded chat entry, link preview autogenerated
            // None of those works well for this case. So, opting out to write
            // a simple temporary text till we have a proper way to show results.
            long? beginningEntryId = foundDocument.Document.Metadata.ChatEntries.Any() ?
                foundDocument.Document.Metadata.ChatEntries.First().LocalId
                : null;
            var link = Links.Chat(foundDocument.Document.Metadata.ChatId, beginningEntryId).ToAbsolute(urlMapper);
            text = $@"
                Here I have something that might be what you're looking for: {link}
                ---
                {foundDocument.Document.Text}
            ";
            //forwardedFrom = chatEntrySlice.Value.Id;
        } else {
            text = NotFoundMessage();
        }

        var upsertCommand = new ChatsBackend_ChangeEntry(
            textEntryId,
            null,
            Change.Create(new ChatEntryDiff {
                AuthorId = botId,
                Content = text,
                ForwardedChatEntryId = forwardedFrom
            }));
        await commander.Call(upsertCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
