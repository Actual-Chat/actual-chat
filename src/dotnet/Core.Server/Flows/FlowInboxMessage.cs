namespace ActualChat.Flows;

// The payload is type-decorated MessagePack, so the message itself can travel through any
// serializer without knowing the payload's type. Id is assigned when the message is stored,
// from the inbox's growing sequence; 0 means not yet.

[DataContract, MessagePackObject]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
public sealed partial record FlowInboxMessage(
    [property: DataMember(Order = 0), Key(0)] long Id,
    [property: DataMember(Order = 1), Key(1)] byte[] Data)
{
    public static IByteSerializer PayloadSerializer => Serializers.MessagePackTypeDecorating;

    private (byte[] Data, object? Payload) _payloadCache;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsStored => Id != 0;

    // Null when the payload can't be read anymore, e.g. its type is gone: such a message can only be removed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public object? Payload {
        get {
            // Keyed by Data, as `with` copies the cache along with whatever Data it sets
            var cache = _payloadCache;
            if (ReferenceEquals(cache.Data, Data))
                return cache.Payload;

            object? payload;
            try {
                payload = PayloadSerializer.Read(Data, typeof(object), out _);
            }
            catch (Exception) {
                payload = null;
            }
            _payloadCache = (Data, payload);
            return payload;
        }
    }

    public static FlowInboxMessage New(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        using var buffer = new ArrayPoolBuffer<byte>(256, false);
        PayloadSerializer.Write(buffer, payload, typeof(object));
        var data = buffer.WrittenSpan.ToArray();
        return new FlowInboxMessage(0, data) { _payloadCache = (data, payload) };
    }

    public override string ToString()
        => $"{nameof(FlowInboxMessage)}(#{Id}, {Data.Length} bytes)";

    // Equality ignores the payload cache
    public bool Equals(FlowInboxMessage? other)
        => other is not null && Id == other.Id && Data.AsSpan().SequenceEqual(other.Data);

    public override int GetHashCode()
        => HashCode.Combine(Id, Data.Length);
}
