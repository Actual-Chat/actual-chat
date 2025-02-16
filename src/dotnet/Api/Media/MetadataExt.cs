namespace ActualChat.Media;

public interface IHasMetadata
{
    PropertyBag Metadata { get; init; }
}

internal static class MetadataExt
{
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCodeAttribute", Justification = "Ok here")]
    [field: AllowNull, MaybeNull]
    private static Action<IHasMetadata, PropertyBag> MetadataSetter
        => field ??= typeof(IHasMetadata).GetProperty("Metadata")!.GetSetter<IHasMetadata, PropertyBag>();

    public static T GetMetadataValue<T>(this IHasMetadata source, T @default = default!, [CallerMemberName] string symbol = "") {
        var value = source.Metadata[symbol];
        if (value == null)
            return @default;

        // TODO: remove this workaround when int is not deserialized as long
        if (typeof(T) == typeof(int))
            value = Convert.ToInt32(value, CultureInfo.InvariantCulture);

        return (T)value;
    }

    public static void SetMetadataValue<T>(this IHasMetadata target, T value, [CallerMemberName] string symbol = "")
        => MetadataSetter.Invoke(target, target.Metadata.Set(symbol, value));
}
