namespace ActualChat.MLSearch;

internal interface IHistoryExtractor
{
    Task<MLSearchChatHistory> Extract(MLSearchChatId chatId, CancellationToken cancellationToken);
}

