namespace ActualChat.Serialization.Internal;

/// <summary>
/// Nerdbank.MessagePack converter for <see cref="Nullable{T}"/>. Wire-compatible with the
/// natural Nerdbank/PolyType OptionalShape encoding: <c>nil</c> for the null case, the
/// underlying T payload for the present case. Exists so codegen-only clients can resolve
/// shapes for arbitrary closed <c>T?</c> referenced only via method type arguments
/// (<c>NewKvasStored&lt;ChatId?&gt;</c> etc.) without enumerating each closed instance.
/// </summary>
public sealed class NullableNerdbankConverter<T> : MessagePackConverter<T?>
    where T : struct
{
    public override T? Read(ref MessagePackReader reader, SerializationContext context)
    {
        if (reader.TryReadNil())
            return null;

        var itemConverter = context.GetConverter<T>(context.TypeShapeProvider);
        return itemConverter.Read(ref reader, context);
    }

    public override void Write(ref MessagePackWriter writer, in T? value, SerializationContext context)
    {
        if (!value.HasValue) {
            writer.WriteNil();
            return;
        }

        var itemConverter = context.GetConverter<T>(context.TypeShapeProvider);
        itemConverter.Write(ref writer, value.Value, context);
    }
}
