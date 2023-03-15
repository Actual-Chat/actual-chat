namespace ActualChat;

[StructLayout(LayoutKind.Auto)]
[DataContract]
public readonly record struct Expiring<T>(
    [property: DataMember(Order = 0)] T Value,
    [property: DataMember(Order = 1)] Moment ExpiresAt = default);
