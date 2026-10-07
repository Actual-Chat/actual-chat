using ActualLab.Versioning;

namespace ActualChat.Flows;

[DataContract, MessagePackObject]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
public sealed partial record FlowInboxDiff(
    [property: DataMember(Order = 0), Key(0)] ApiArray<FlowInboxMessage> Added,
    [property: DataMember(Order = 1), Key(1)] ApiArray<long> RemovedIds)
{
    public static readonly FlowInboxDiff Empty = new([], []);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsEmpty => Added.Count == 0 && RemovedIds.Count == 0;

    // Returns the same messages when nothing changes, otherwise allocates just the resulting array
    public ApiArray<FlowInboxMessage> ApplyTo(
        ApiArray<FlowInboxMessage> messages,
        ref long lastId,
        VersionGenerator<long> idGenerator)
    {
        // The messages are ordered by Id, and every added one gets an Id after all of them,
        // so appending the added ones keeps the order. Whatever Id an added message carried is ignored.
        var removedIds = RemovedIds.Count > 8 ? RemovedIds.ToHashSet() : null;
        var removedCount = 0;
        foreach (var message in messages)
            if (IsRemoved(message.Id))
                removedCount++;
        if (removedCount == 0 && Added.Count == 0)
            return messages;

        var result = new FlowInboxMessage[messages.Count - removedCount + Added.Count];
        var index = 0;
        foreach (var message in messages)
            if (!IsRemoved(message.Id))
                result[index++] = message;
        foreach (var message in Added) {
            lastId = idGenerator.NextVersion(lastId);
            result[index++] = message with { Id = lastId };
        }
        return new ApiArray<FlowInboxMessage>(result);

        bool IsRemoved(long id)
            => removedIds?.Contains(id) ?? RemovedIds.Items.Contains(id);
    }
}
