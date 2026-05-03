namespace ActualChat.Media;

[StructLayout(LayoutKind.Auto)]
[DataContract, MemoryPackable, MessagePackObject]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
public readonly partial record struct Size(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] int Width,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] int Height)
{
    // Computed properties

    [property: JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreMember]
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => $"({Width} x {Height})";

    // Conversion

    public static implicit operator Size((int Width, int Height) tuple) => new(tuple.Width, tuple.Height);
}
