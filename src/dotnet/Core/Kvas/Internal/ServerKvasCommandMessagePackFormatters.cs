using MessagePack.Formatters;

namespace ActualChat.Kvas.Internal;

// Wire-compatible map-format formatters for the ServerKvas/ServerSettings commands.
// A missing Uuid entry reads back as "" - the value ApiCommandRpcArgumentSerializer prepends for legacy peers.

// ReSharper disable once InconsistentNaming
public sealed class ServerKvas_SetMessagePackFormatter : IMessagePackFormatter<ServerKvas_Set?>
{
    public void Serialize(ref MessagePackWriter writer, ServerKvas_Set? value, MessagePackSerializerOptions options)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }

        writer.WriteMapHeader(4);
        writer.Write(nameof(ServerKvas_Set.Uuid));
        writer.Write(value.Uuid);
        writer.Write(nameof(ServerKvas_Set.Session));
        options.Resolver.GetFormatterWithVerify<Session>().Serialize(ref writer, value.Session, options);
        writer.Write(nameof(ServerKvas_Set.Key));
        options.Resolver.GetFormatterWithVerify<string>().Serialize(ref writer, value.Key, options);
        writer.Write(nameof(ServerKvas_Set.Value));
        options.Resolver.GetFormatterWithVerify<byte[]?>().Serialize(ref writer, value.Value, options);
    }

    public ServerKvas_Set? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;

        var mapLen = reader.ReadMapHeader();
        var uuid = "";
        Session session = default!;
        var key = "";
        byte[]? value = null;
        for (var i = 0; i < mapLen; i++) {
            var k = reader.ReadString();
            switch (k) {
                case nameof(ServerKvas_Set.Uuid):
                    uuid = reader.ReadString() ?? "";
                    break;
                case nameof(ServerKvas_Set.Session):
                    session = options.Resolver.GetFormatterWithVerify<Session>().Deserialize(ref reader, options);
                    break;
                case nameof(ServerKvas_Set.Key):
                    key = options.Resolver.GetFormatterWithVerify<string>().Deserialize(ref reader, options);
                    break;
                case nameof(ServerKvas_Set.Value):
                    value = options.Resolver.GetFormatterWithVerify<byte[]?>().Deserialize(ref reader, options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new ServerKvas_Set { Uuid = uuid, Session = session, Key = key!, Value = value };
    }
}

// ReSharper disable once InconsistentNaming
public sealed class ServerKvas_SetManyMessagePackFormatter : IMessagePackFormatter<ServerKvas_SetMany?>
{
    public void Serialize(ref MessagePackWriter writer, ServerKvas_SetMany? value, MessagePackSerializerOptions options)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }

        writer.WriteMapHeader(3);
        writer.Write(nameof(ServerKvas_SetMany.Uuid));
        writer.Write(value.Uuid);
        writer.Write(nameof(ServerKvas_SetMany.Session));
        options.Resolver.GetFormatterWithVerify<Session>().Serialize(ref writer, value.Session, options);
        writer.Write(nameof(ServerKvas_SetMany.Items));
        options.Resolver.GetFormatterWithVerify<(string Key, byte[]? Value)[]>()
            .Serialize(ref writer, value.Items, options);
    }

    public ServerKvas_SetMany? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;

        var mapLen = reader.ReadMapHeader();
        var uuid = "";
        Session session = default!;
        (string Key, byte[]? Value)[] items = [];
        for (var i = 0; i < mapLen; i++) {
            var k = reader.ReadString();
            switch (k) {
                case nameof(ServerKvas_SetMany.Uuid):
                    uuid = reader.ReadString() ?? "";
                    break;
                case nameof(ServerKvas_SetMany.Session):
                    session = options.Resolver.GetFormatterWithVerify<Session>().Deserialize(ref reader, options);
                    break;
                case nameof(ServerKvas_SetMany.Items):
                    items = options.Resolver.GetFormatterWithVerify<(string Key, byte[]? Value)[]>()
                        .Deserialize(ref reader, options) ?? [];
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new ServerKvas_SetMany { Uuid = uuid, Session = session, Items = items };
    }
}

// ReSharper disable once InconsistentNaming
public sealed class ServerKvas_MigrateGuestKeysMessagePackFormatter
    : IMessagePackFormatter<ServerKvas_MigrateGuestKeys?>
{
    public void Serialize(
        ref MessagePackWriter writer,
        ServerKvas_MigrateGuestKeys? value,
        MessagePackSerializerOptions options)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }

        writer.WriteMapHeader(2);
        writer.Write(nameof(ServerKvas_MigrateGuestKeys.Uuid));
        writer.Write(value.Uuid);
        writer.Write(nameof(ServerKvas_MigrateGuestKeys.Session));
        options.Resolver.GetFormatterWithVerify<Session>().Serialize(ref writer, value.Session, options);
    }

    public ServerKvas_MigrateGuestKeys? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;

        var mapLen = reader.ReadMapHeader();
        var uuid = "";
        Session session = default!;
        for (var i = 0; i < mapLen; i++) {
            var k = reader.ReadString();
            switch (k) {
                case nameof(ServerKvas_MigrateGuestKeys.Uuid):
                    uuid = reader.ReadString() ?? "";
                    break;
                case nameof(ServerKvas_MigrateGuestKeys.Session):
                    session = options.Resolver.GetFormatterWithVerify<Session>().Deserialize(ref reader, options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new ServerKvas_MigrateGuestKeys { Uuid = uuid, Session = session };
    }
}

// ReSharper disable once InconsistentNaming
public sealed class ServerSettings_SetMessagePackFormatter : IMessagePackFormatter<ServerSettings_Set?>
{
    public void Serialize(ref MessagePackWriter writer, ServerSettings_Set? value, MessagePackSerializerOptions options)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }

        writer.WriteMapHeader(4);
        writer.Write(nameof(ServerSettings_Set.Uuid));
        writer.Write(value.Uuid);
        writer.Write(nameof(ServerSettings_Set.Session));
        options.Resolver.GetFormatterWithVerify<Session>().Serialize(ref writer, value.Session, options);
        writer.Write(nameof(ServerSettings_Set.Key));
        options.Resolver.GetFormatterWithVerify<string>().Serialize(ref writer, value.Key, options);
        writer.Write(nameof(ServerSettings_Set.Value));
        options.Resolver.GetFormatterWithVerify<byte[]?>().Serialize(ref writer, value.Value, options);
    }

    public ServerSettings_Set? Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return null;

        var mapLen = reader.ReadMapHeader();
        var uuid = "";
        Session session = default!;
        var key = "";
        byte[]? value = null;
        for (var i = 0; i < mapLen; i++) {
            var k = reader.ReadString();
            switch (k) {
                case nameof(ServerSettings_Set.Uuid):
                    uuid = reader.ReadString() ?? "";
                    break;
                case nameof(ServerSettings_Set.Session):
                    session = options.Resolver.GetFormatterWithVerify<Session>().Deserialize(ref reader, options);
                    break;
                case nameof(ServerSettings_Set.Key):
                    key = options.Resolver.GetFormatterWithVerify<string>().Deserialize(ref reader, options);
                    break;
                case nameof(ServerSettings_Set.Value):
                    value = options.Resolver.GetFormatterWithVerify<byte[]?>().Deserialize(ref reader, options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new ServerSettings_Set { Uuid = uuid, Session = session, Key = key!, Value = value };
    }
}
