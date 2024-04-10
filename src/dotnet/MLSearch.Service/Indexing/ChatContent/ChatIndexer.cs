
using ActualChat.Chat;
using ActualChat.MLSearch.Documents;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IChatIndexer
{
    Task InitAsync(CancellationToken cancellationToken);
    ValueTask ApplyAsync(ChatEvent @event, CancellationToken cancellationToken);
    Task<ChatEntryCursor> FlushAsync(CancellationToken cancellationToken);
}

internal sealed class ChatIndexer(
    IChatEntryLoader chatEntryLoader,
    IDocumentLoader documentLoader,
    IDocumentMapper<ChatEntry, ChatEntry, ChatSlice> documentMapper,
    ISink<ChatSlice, string> sink
) : IChatIndexer
{
    private ChatEntryCursor _cursor = new(0, 0);
    private readonly IList<ChatEntry> _creates = new List<ChatEntry>();
    private readonly IList<ChatEntry> _updates = new List<ChatEntry>();
    private readonly IList<ChatEntry> _deletes = new List<ChatEntry>();

    public Task InitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public ValueTask ApplyAsync(ChatEvent @event, CancellationToken cancellationToken)
    {
        var evtGroup = @event.Type switch {
            ChatEventType.New => _creates,
            ChatEventType.Update => _updates,
            ChatEventType.Remove => _deletes,
            _ => throw StandardError.NotSupported($"The event type {@event.Type} is not supported."),
        };
        evtGroup.Add(@event.ChatEntry);
        _cursor = @event.Id;
        return ValueTask.CompletedTask;
    }

    public async Task<ChatEntryCursor> FlushAsync(CancellationToken cancellationToken)
    {
        var updatedDocuments = _creates.Concat(_updates).Select(documentMapper.Map);
        var deletedDocuments = _deletes.Select(documentMapper.MapId);
        await sink.ExecuteAsync(updatedDocuments, deletedDocuments, cancellationToken).ConfigureAwait(false);
        _creates.Clear();
        _updates.Clear();
        _deletes.Clear();
        return _cursor;
    }
}
