using MessagePack;
using MessagePack.Formatters;

namespace ActualChat.Internal;

public sealed class StringIdentifierMessagePackFormatter<TId> : IMessagePackFormatter<TId>
    where TId : StringIdentifier, IStringIdentifier<TId>
{
    public void Serialize(ref MessagePackWriter writer, TId? value, MessagePackSerializerOptions options)
        => options.Resolver.GetFormatterWithVerify<string?>().Serialize(ref writer, value?.Value, options);

    public TId Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var s = options.Resolver.GetFormatterWithVerify<string?>().Deserialize(ref reader, options);
        return s.IsNullOrEmpty() ? null! : TId.Parse(s);
    }
}
