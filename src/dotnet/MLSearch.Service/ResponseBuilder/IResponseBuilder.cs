using ActualChat.MLSearch.Engine;

namespace ActualChat.MLSearch;

internal interface IResponseBuilder
{
    Task<MLSearchResponse> Build<TDocument>(
        MLSearchChatHistory history, VectorSearchResult<TDocument> searchResult, CancellationToken cancellationToken)
        where TDocument: class;
}
