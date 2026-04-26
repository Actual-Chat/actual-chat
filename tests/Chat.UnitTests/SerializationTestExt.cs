namespace ActualChat.Chat.UnitTests;

internal static class SerializationTestExt
{
    /// <summary>
    /// Round-trips <paramref name="value"/> through MessagePack — the only wire format
    /// new ChatEntry / Invite union-shaped types support. JSON/MemoryPack are skipped
    /// because System.Text.Json can't deserialize abstract records without explicit
    /// polymorphism config and MemoryPack is reserved for the legacy types.
    /// </summary>
    public static T PassThroughModernSerializers<T>(this T value, ITestOutputHelper? output = null)
        => value.PassThroughMessagePackByteSerializer(output);
}
