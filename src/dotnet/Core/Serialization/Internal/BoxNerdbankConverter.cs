using ActualLab.Serialization;

namespace ActualChat.Serialization.Internal;

/// <summary>
/// Nerdbank.MessagePack converter for <see cref="Box{T}"/>. Strips the box on the wire — only
/// the inner <typeparamref name="T"/> value is written/read, no envelope. This makes
/// <c>Box&lt;T&gt;</c> wire-compatible with a bare <c>T</c> payload, which is what the
/// previous <c>BoxMessagePackFormatter</c> produced. A null <c>Box&lt;T&gt;</c> writes a
/// single <c>nil</c>.
/// </summary>
public sealed class BoxNerdbankConverter<T> : MessagePackConverter<Box<T>?>
{
    public override Box<T>? Read(ref MessagePackReader reader, SerializationContext context)
    {
        if (reader.TryReadNil())
            return null;

        var itemConverter = context.GetConverter<T>(context.TypeShapeProvider);
        return new Box<T>(itemConverter.Read(ref reader, context)!);
    }

    public override void Write(ref MessagePackWriter writer, in Box<T>? value, SerializationContext context)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }

        var itemConverter = context.GetConverter<T>(context.TypeShapeProvider);
        itemConverter.Write(ref writer, value.Value, context);
    }
}
