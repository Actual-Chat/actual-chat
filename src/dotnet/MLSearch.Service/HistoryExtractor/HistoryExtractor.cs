namespace ActualChat.MLSearch;

internal class HistoryExtractor : IHistoryExtractor
{
    public Task<MLSearchChatHistory> Extract(MLSearchChatId chatId, CancellationToken cancellationToken)
    {
        // Extracts either entire history
        // or in some cases up to the moment when User did search reset
        throw new NotImplementedException();
    }
}

