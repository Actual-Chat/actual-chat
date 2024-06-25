namespace ActualChat.MLSearch.Indexing;
internal interface IChatIndexFilter {
    Task<bool> ChatShouldBeIndexed(ChatId chatId, CancellationToken cancellationToken);
}