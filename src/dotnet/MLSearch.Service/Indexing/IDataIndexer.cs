namespace ActualChat.MLSearch.Indexing;

/// <summary>
/// Data indexing result type.
/// </summary>
/// <param name="IsEndReached">
/// It is set to True if the end of the currenly available stream has been reached.
/// </param>
internal record DataIndexerResult(bool IsEndReached);

/// <summary>
/// Controls indexing of a single stream source of documents.
/// It is independent of underlying engines and depends on
/// the documents source type.
/// It is responsible for:
/// - fetching documents from the source
/// - converting documents into an inner format and forwarding
///   them downstream
/// - saving the indexing progress
///
/// Notes:
/// This is what HistoryExtractor was intended for.
/// Compared to the HistoryExtactor where it enforced
/// one type of documents (MLSearchChatHistory) to be accepted
/// downstream, this interface does not put any obligations on
/// how the forwarding process is implemented. It is assumed
/// though that the Engine dependent implementation will be
/// separated into another entity (ISink).
/// </summary>
/// <typeparam name="T">Target stream identifier.</typeparam>
internal interface IDataIndexer<in T>
{
    Task<DataIndexerResult> IndexNextAsync(T targetId, CancellationToken cancellationToken);
}
