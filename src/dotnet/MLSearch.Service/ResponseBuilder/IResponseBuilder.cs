namespace ActualChat.MLSearch;

internal interface IResponseBuilder
{
    Task<MLSearchResponse> Build(MLSearchChatHistory history, VectorSearchResult searchResult, CancellationToken cancellationToken);
}
