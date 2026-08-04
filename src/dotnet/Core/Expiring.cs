using ActualChat.Serialization.Internal;

namespace ActualChat;

[DataContract, MessagePackFormatter(typeof(ExpiringMessagePackFormatter<>))]
public sealed partial record Expiring<T>(
    [property: DataMember(Order = 0)] T Value,
    [property: DataMember(Order = 1)] Moment ExpiresAt = default);
