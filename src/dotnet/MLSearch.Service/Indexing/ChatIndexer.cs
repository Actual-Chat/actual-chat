
using ActualChat.Chat;
using ActualChat.MLSearch.Engine.Indexing;

namespace ActualChat.MLSearch.Indexing;

internal interface IChatIndexer
{
    Task InitAsync(CancellationToken cancellationToken);
    ValueTask ApplyAsync(ChatEvent @event, CancellationToken cancellationToken);
    Task<ChatEntryCursor> FlushAsync(CancellationToken cancellationToken);
}

internal sealed class ChatIndexer(
    ISink<ChatEntry, ChatEntry> sink
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
        };
        evtGroup.Add(@event.ChatEntry);
        _cursor = @event.Id;
        return ValueTask.CompletedTask;
    }

    public async Task<ChatEntryCursor> FlushAsync(CancellationToken cancellationToken)
    {
        await sink.ExecuteAsync(_creates.Concat(_updates), _deletes, cancellationToken).ConfigureAwait(false);
        return _cursor;
    }
}
