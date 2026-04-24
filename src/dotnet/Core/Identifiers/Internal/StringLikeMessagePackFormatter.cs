using MessagePack.Formatters;

namespace ActualChat.Internal;

public sealed class StringLikeMessagePackFormatter<T> : IMessagePackFormatter<T>
    where T : IStringLike<T>
{
    public void Serialize(ref MessagePackWriter writer, T? value, MessagePackSerializerOptions options)
        => options.Resolver.GetFormatterWithVerify<string?>().Serialize(ref writer, value?.Value, options);

    public T Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var s = options.Resolver.GetFormatterWithVerify<string?>().Deserialize(ref reader, options);
        return s.IsNullOrEmpty() ? default! : T.Parse(s);
    }
}
