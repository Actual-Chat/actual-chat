namespace ActualChat.Flows;

// The inbox an IInboxProcessingFlow sees during a resume: the messages stored when it started,
// plus whatever the flow added or removed since. The flow's commit stores these changes together
// with its state, and only them - messages posted meanwhile stay in the inbox. Messages the flow
// adds have no Id until they are stored, and it sees them with their Ids on its next resume.

public sealed class FlowInbox
{
    private readonly FlowInboxMessage[] _stored;
    private List<FlowInboxMessage>? _messages; // A copy of _stored, made on the first change
    private List<FlowInboxMessage>? _added;
    private HashSet<long>? _removedIds;

    public IReadOnlyList<FlowInboxMessage> Messages => _messages ?? (IReadOnlyList<FlowInboxMessage>)_stored;
    public int Count => _messages?.Count ?? _stored.Length;
    public bool HasChanges => _added is { Count: > 0 } || _removedIds is { Count: > 0 };

    // The stored messages: ordered by Id, all of them with one
    public FlowInbox(ApiArray<FlowInboxMessage> messages)
        => _stored = messages.Items;

    public override string ToString()
        => $"{nameof(FlowInbox)}({Count} message(s), {_added?.Count ?? 0} added, {_removedIds?.Count ?? 0} removed)";

    public FlowInboxMessage Add(object payload)
    {
        var message = FlowInboxMessage.New(payload);
        GetMessages().Add(message);
        (_added ??= new()).Add(message);
        return message;
    }

    public bool Remove(FlowInboxMessage message)
    {
        if (message.IsStored)
            return Remove(message.Id);

        // Not stored yet, so it is just not added after all
        if (_added is null || !_added.Remove(message))
            return false;

        GetMessages().Remove(message);
        return true;
    }

    public bool Remove(long id)
    {
        var index = IndexOfStored(id);
        if (index < 0)
            return false;

        GetMessages().RemoveAt(index);
        (_removedIds ??= new()).Add(id);
        return true;
    }

    public void Clear()
    {
        foreach (var message in Messages.ToList())
            Remove(message);
    }

    public FlowInboxDiff GetDiff()
        => HasChanges
            ? new FlowInboxDiff(
                _added is null ? [] : new ApiArray<FlowInboxMessage>(_added.ToArray()),
                _removedIds is null ? [] : new ApiArray<long>(_removedIds.Order().ToArray()))
            : FlowInboxDiff.Empty;

    public void AcceptChanges()
    {
        // The added messages are stored now, under Ids this copy doesn't know
        if (_added is not null)
            _messages!.RemoveAll(x => !x.IsStored);
        _added = null;
        _removedIds = null;
    }

    // Private methods

    private List<FlowInboxMessage> GetMessages()
        => _messages ??= new List<FlowInboxMessage>(_stored);

    private int IndexOfStored(long id)
    {
        var messages = Messages;
        for (var i = 0; i < messages.Count; i++)
            if (messages[i].Id == id && messages[i].IsStored)
                return i;

        return -1;
    }
}
