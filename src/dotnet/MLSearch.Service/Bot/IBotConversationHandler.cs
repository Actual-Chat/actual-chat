using ActualChat.Chat;

namespace ActualChat.MLSearch.Bot;

internal interface IBotConversationHandler
{
    Task ExecuteAsync(
        IReadOnlyList<ChatEntry>? updatedDocuments,
        IReadOnlyCollection<ChatEntryId>? deletedDocuments,
        CancellationToken cancellationToken = default);
}
