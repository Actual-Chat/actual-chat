using ActualLab.Api.Internal;
using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

/// <summary>
/// <see cref="ApiArrayMessagePackFormatter{T}"/> that reads nil as <see cref="ApiArray{T}.Empty"/>.
/// For an <see cref="ApiArray{T}"/> member added at a key that older array-form payloads hold as nil.
/// </summary>
public sealed class NilTolerantApiArrayMessagePackFormatter<T> : IMessagePackFormatter<ApiArray<T>>
{
    private static readonly ApiArrayMessagePackFormatter<T> Inner = new();

    public void Serialize(ref MessagePackWriter writer, ApiArray<T> value, MessagePackSerializerOptions options)
        => Inner.Serialize(ref writer, value, options);

    public ApiArray<T> Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        => reader.TryReadNil() ? ApiArray<T>.Empty : Inner.Deserialize(ref reader, options);
}
