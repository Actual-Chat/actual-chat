using ActualChat.MLSearch.Engine;

namespace ActualChat.MLSearch;

internal class ResponseBuilder : IResponseBuilder
{
    public Task<MLSearchResponse> Build<TDocument>(
        MLSearchChatHistory history, SearchResult<TDocument> searchResult, CancellationToken cancellationToken)
        where TDocument : class
    {
        // Generates summaries for individual docs
        // Generates overall summary
        // Makes highlights
        // Probably implements re-ranking
        throw new NotImplementedException();
    }
}
