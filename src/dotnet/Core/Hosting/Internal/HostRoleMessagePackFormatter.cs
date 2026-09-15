using MessagePack.Formatters;

namespace ActualChat.Hosting.Internal;

// Serializes HostRole as a plain MessagePack string — same wire shape as a
// StringIdentifier/SymbolIdentifier formatter, just for a type that doesn't
// implement ISymbolIdentifier.
public sealed class HostRoleMessagePackFormatter : IMessagePackFormatter<HostRole>
{
    public void Serialize(ref MessagePackWriter writer, HostRole value, MessagePackSerializerOptions options)
        => options.Resolver.GetFormatterWithVerify<string>().Serialize(ref writer, value.Id.Value, options);

    public HostRole Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var s = options.Resolver.GetFormatterWithVerify<string>().Deserialize(ref reader, options);
        return s is null ? default : new HostRole(new Symbol(s));
    }
}
