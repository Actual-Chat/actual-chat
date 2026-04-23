namespace ActualChat.Serialization.Internal;

/// <summary>
/// Nerdbank.MessagePack converter for <see cref="Maybe{T}"/>. Three-state encoding mirrors
/// <see cref="OptionNerdbankConverter{T}"/> but extended to cover the null reference case
/// since <c>Maybe&lt;T&gt;</c> is a class:
/// <list type="bullet">
///   <item><c>nil</c> — the <c>Maybe&lt;T&gt;</c> reference itself was null;</item>
///   <item><c>[]</c> (zero-length array) — <see cref="Maybe.None{T}"/>;</item>
///   <item><c>[value]</c> (one-length array) — <see cref="Maybe.Value{T}"/>.</item>
/// </list>
/// </summary>
public sealed class MaybeNerdbankConverter<T> : MessagePackConverter<Maybe<T>?>
{
    public override Maybe<T>? Read(ref MessagePackReader reader, SerializationContext context)
    {
        if (reader.TryReadNil())
            return null;

        var count = reader.ReadArrayHeader();
        if (count == 0)
            return new Maybe<T>(false, default);
        if (count != 1)
            throw new MessagePackSerializationException($"Expected 0 or 1 items for Maybe<>, but got {count}.");

        var itemConverter = context.GetConverter<T>(context.TypeShapeProvider);
        return new Maybe<T>(true, itemConverter.Read(ref reader, context));
    }

    public override void Write(ref MessagePackWriter writer, in Maybe<T>? value, SerializationContext context)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }
        if (!value.HasValue) {
            writer.WriteArrayHeader(0);
            return;
        }

        writer.WriteArrayHeader(1);
        var itemConverter = context.GetConverter<T>(context.TypeShapeProvider);
        itemConverter.Write(ref writer, value.ValueOrDefault!, context);
    }
}
