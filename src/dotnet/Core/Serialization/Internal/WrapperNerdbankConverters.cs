using System.Numerics;

namespace ActualChat.Serialization.Internal;

/// <summary>
/// Nerdbank.MessagePack converter for <see cref="Change{TCreate,TUpdate}"/>. Legacy wire
/// format: <c>[Option&lt;TCreate&gt;, Option&lt;TUpdate&gt;, bool]</c> — matches
/// <c>ChangeMessagePackFormatter</c>.
/// </summary>
public sealed class ChangeNerdbankConverter<TCreate, TUpdate> : MessagePackConverter<Change<TCreate, TUpdate>?>
{
    public override Change<TCreate, TUpdate>? Read(ref MessagePackReader reader, SerializationContext context)
    {
        if (reader.TryReadNil())
            return null;
        var count = reader.ReadArrayHeader();
        if (count != 3)
            throw new MessagePackSerializationException($"Expected 3 items for Change<,>, but got {count}.");
        var createConverter = context.GetConverter<Option<TCreate>>(context.TypeShapeProvider);
        var updateConverter = context.GetConverter<Option<TUpdate>>(context.TypeShapeProvider);
        var create = createConverter.Read(ref reader, context);
        var update = updateConverter.Read(ref reader, context);
        var remove = reader.ReadBoolean();
        return new Change<TCreate, TUpdate> { Create = create, Update = update, Remove = remove };
    }

    public override void Write(ref MessagePackWriter writer, in Change<TCreate, TUpdate>? value, SerializationContext context)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }
        writer.WriteArrayHeader(3);
        var createConverter = context.GetConverter<Option<TCreate>>(context.TypeShapeProvider);
        var updateConverter = context.GetConverter<Option<TUpdate>>(context.TypeShapeProvider);
        createConverter.Write(ref writer, value.Create, context);
        updateConverter.Write(ref writer, value.Update, context);
        writer.Write(value.Remove);
    }
}

/// <summary>
/// Nerdbank.MessagePack converter for <see cref="Change{T}"/>. Delegates to the
/// <c>Change&lt;T, T&gt;</c> converter — the single-type-arg variant is a convenience
/// wrapper with identical wire shape.
/// </summary>
public sealed class ChangeNerdbankConverter<T> : MessagePackConverter<Change<T>?>
{
    private readonly ChangeNerdbankConverter<T, T> _inner = new();

    public override Change<T>? Read(ref MessagePackReader reader, SerializationContext context)
    {
        var inner = _inner.Read(ref reader, context);
        return inner is null ? null : new Change<T> {
            Create = inner.Create,
            Update = inner.Update,
            Remove = inner.Remove,
        };
    }

    public override void Write(ref MessagePackWriter writer, in Change<T>? value, SerializationContext context)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }
        // Cast semantically to Change<T, T>; safe because both share the same primary ctor-like surface.
        var twoArg = new Change<T, T> { Create = value.Create, Update = value.Update, Remove = value.Remove };
        _inner.Write(ref writer, twoArg, context);
    }
}

/// <summary>
/// Nerdbank.MessagePack converter for <see cref="Expiring{T}"/>: <c>[Value, ExpiresAt]</c>.
/// </summary>
public sealed class ExpiringNerdbankConverter<T> : MessagePackConverter<Expiring<T>?>
{
    public override Expiring<T>? Read(ref MessagePackReader reader, SerializationContext context)
    {
        if (reader.TryReadNil())
            return null;
        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Expected 2 items for Expiring<>, but got {count}.");
        var valueConverter = context.GetConverter<T>(context.TypeShapeProvider);
        var momentConverter = context.GetConverter<Moment>(context.TypeShapeProvider);
        var value = valueConverter.Read(ref reader, context)!;
        var expiresAt = momentConverter.Read(ref reader, context);
        return new Expiring<T>(value, expiresAt);
    }

    public override void Write(ref MessagePackWriter writer, in Expiring<T>? value, SerializationContext context)
    {
        if (value is null) {
            writer.WriteNil();
            return;
        }
        writer.WriteArrayHeader(2);
        var valueConverter = context.GetConverter<T>(context.TypeShapeProvider);
        var momentConverter = context.GetConverter<Moment>(context.TypeShapeProvider);
        valueConverter.Write(ref writer, value.Value, context);
        momentConverter.Write(ref writer, value.ExpiresAt, context);
    }
}

/// <summary>
/// Nerdbank.MessagePack converter for <see cref="Trimmed{T}"/>: <c>[Value, Limit]</c> where
/// <c>Limit</c> is <c>Nullable&lt;T&gt;</c> — distinct from <c>T</c> because of the struct
/// constraint, mirroring the MessagePack-CSharp SG output.
/// </summary>
public sealed class TrimmedNerdbankConverter<T> : MessagePackConverter<Trimmed<T>>
    where T : struct, IAdditionOperators<T, T, T>, IComparable<T>, IEquatable<T>
{
    public override Trimmed<T> Read(ref MessagePackReader reader, SerializationContext context)
    {
        var count = reader.ReadArrayHeader();
        if (count != 2)
            throw new MessagePackSerializationException($"Expected 2 items for Trimmed<>, but got {count}.");
        var valueConverter = context.GetConverter<T>(context.TypeShapeProvider);
        var limitConverter = context.GetConverter<T?>(context.TypeShapeProvider);
        var value = valueConverter.Read(ref reader, context);
        var limit = limitConverter.Read(ref reader, context);
        return new Trimmed<T>(value, limit);
    }

    public override void Write(ref MessagePackWriter writer, in Trimmed<T> value, SerializationContext context)
    {
        writer.WriteArrayHeader(2);
        var valueConverter = context.GetConverter<T>(context.TypeShapeProvider);
        var limitConverter = context.GetConverter<T?>(context.TypeShapeProvider);
        valueConverter.Write(ref writer, value.Value, context);
        limitConverter.Write(ref writer, value.Limit, context);
    }
}
