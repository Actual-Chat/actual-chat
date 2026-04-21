using MessagePack.Formatters;

namespace ActualChat.Security.Internal;

public sealed class DecryptedSecureTokenMessagePackFormatter : IMessagePackFormatter<DecryptedSecureToken?>
{
    public void Serialize(ref MessagePackWriter writer, DecryptedSecureToken? value, MessagePackSerializerOptions options)
    {
        if (value is null) { writer.WriteNil(); return; }
        writer.WriteMapHeader(2);
        writer.Write(nameof(DecryptedSecureToken.Value));
        options.Resolver.GetFormatterWithVerify<string>().Serialize(ref writer, value.Value, options);
        writer.Write(nameof(DecryptedSecureToken.ExpiresAt));
        options.Resolver.GetFormatterWithVerify<Moment>().Serialize(ref writer, value.ExpiresAt, options);
    }

    public DecryptedSecureToken? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;
        var mapLen = reader.ReadMapHeader();
        var val = "";
        Moment expiresAt = default;
        for (var i = 0; i < mapLen; i++) {
            var key = reader.ReadString();
            switch (key) {
                case nameof(DecryptedSecureToken.Value):
                    val = options.Resolver.GetFormatterWithVerify<string>().Deserialize(ref reader, options);
                    break;
                case nameof(DecryptedSecureToken.ExpiresAt):
                    expiresAt = options.Resolver.GetFormatterWithVerify<Moment>().Deserialize(ref reader, options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return new DecryptedSecureToken(val!, expiresAt);
    }
}
