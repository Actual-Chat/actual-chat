using ActualChat.MLSearch.Engine;

namespace ActualChat.MLSearch;

internal interface IResponseBuilder
{
    Task<MLSearchResponse> Build<TDocument>(
        MLSearchChatHistory history, SearchResult<TDocument> searchResult, CancellationToken cancellationToken)
        where TDocument: class;
}
