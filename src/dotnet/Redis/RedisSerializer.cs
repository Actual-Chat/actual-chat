using StackExchange.Redis;

namespace ActualChat.Redis;

/// <summary>
/// A thin wrapper around <see cref="IByteSerializer"/> that converts values
/// to/from <see cref="RedisValue"/>, handling null/empty payloads uniformly.
/// </summary>
public readonly struct RedisSerializer(IByteSerializer serializer)
{
    public static readonly RedisSerializer Default = new(MessagePackByteSerializer.Default);

    public IByteSerializer Serializer { get; } = serializer;

    public RedisValue Write<TValue>(TValue value)
    {
        if (value is null)
            return Array.Empty<byte>();

        using var buffer = Serializer.Write(value, typeof(TValue));
        return (RedisValue)buffer.WrittenSpan.ToArray();
    }

    public TValue? Read<TValue>(RedisValue value)
    {
        if (value.IsNullOrEmpty)
            return default;
        var bytes = (byte[])value!;
        if (bytes.Length == 0)
            return default;

        return (TValue?)Serializer.Read(bytes, typeof(TValue), out _);
    }
}
