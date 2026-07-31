namespace ActualChat.Serialization;

/// <summary>
/// The type schema behind <c>MetadataBag</c>: everything
/// <see cref="TypeSchema.PrimitiveOnly"/> allows, plus <see cref="Size2D"/>.
/// </summary>
public sealed class MetadataSchema : TypeSchema
{
    private static readonly TypeSchema Primitives = new PrimitiveOnly();

    public override bool IsAllowed(Type type)
        => Primitives.IsAllowed(type) || type == typeof(Size2D);
}
