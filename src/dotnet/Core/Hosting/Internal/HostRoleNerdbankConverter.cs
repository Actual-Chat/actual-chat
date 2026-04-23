
namespace ActualChat.Hosting.Internal;

/// <summary>
/// Nerdbank.MessagePack converter for <see cref="HostRole"/>: wire format is a plain msgpack
/// string (the underlying <see cref="Symbol"/> value), matching <see cref="HostRoleMessagePackFormatter"/>.
/// </summary>
public sealed class HostRoleNerdbankConverter : MessagePackConverter<HostRole>
{
    public override HostRole Read(ref MessagePackReader reader, SerializationContext context)
    {
        if (reader.TryReadNil())
            return default;
        var s = reader.ReadString();
        return s is null ? default : new HostRole(new Symbol(s));
    }

    public override void Write(ref MessagePackWriter writer, in HostRole value, SerializationContext context)
        => writer.Write(value.Id.Value);
}
