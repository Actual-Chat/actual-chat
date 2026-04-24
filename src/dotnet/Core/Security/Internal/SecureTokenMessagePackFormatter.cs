using MessagePack.Formatters;

namespace ActualChat.Security.Internal;

// Wire-compatible map-format formatter ([MessagePackObject(true)] produces a map).
public sealed class SecureTokenMessagePackFormatter : IMessagePackFormatter<SecureToken?>
{
    public void Serialize(ref MessagePackWriter writer, SecureToken? value, MessagePackSerializerOptions options)
    {
        if (value is null) { writer.WriteNil(); return; }
        writer.WriteMapHeader(2);
        writer.Write(nameof(SecureToken.Token));
        options.Resolver.GetFormatterWithVerify<string>().Serialize(ref writer, value.Token, options);
        writer.Write(nameof(SecureToken.ExpiresAt));
        options.Resolver.GetFormatterWithVerify<Moment>().Serialize(ref writer, value.ExpiresAt, options);
    }

    public SecureToken? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;
        var mapLen = reader.ReadMapHeader();
        var token = "";
        Moment expiresAt = default;
        for (var i = 0; i < mapLen; i++) {
            var key = reader.ReadString();
            switch (key) {
                case nameof(SecureToken.Token):
                    token = options.Resolver.GetFormatterWithVerify<string>().Deserialize(ref reader, options);
                    break;
                case nameof(SecureToken.ExpiresAt):
                    expiresAt = options.Resolver.GetFormatterWithVerify<Moment>().Deserialize(ref reader, options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return new SecureToken(token!, expiresAt);
    }
}
