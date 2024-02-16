namespace ActualChat.MLSearch;

internal class ResponseBuilder : IResponseBuilder
{
    public Task<MLSearchResponse> Build(MLSearchChatHistory history, VectorSearchResult searchResult, CancellationToken cancellationToken)
    {
        // Generates summaries for individual docs
        // Generates overall summary
        // Makes highlights
        // Probably implements re-ranking
        throw new NotImplementedException();
    }
}
