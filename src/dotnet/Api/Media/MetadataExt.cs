namespace ActualChat.Media;

public interface IHasMetadata
{
    ImmutableOptionSet Metadata { get; set; }
}

internal static class MetadataExt
{
    public static T GetMetadataValue<T>(this IHasMetadata hasMetadata, T @default = default!, [CallerMemberName] string symbol = "") {
        var value = hasMetadata.Metadata[symbol];
        if (value == null)
            return @default;

        // TODO: remove this workaround when int is not deserialized as long
        if (typeof(T) == typeof(int))
            value = Convert.ToInt32(value, CultureInfo.InvariantCulture);

        return (T)value;
    }

    public static void SetMetadataValue<T>(this IHasMetadata metadata, T value, [CallerMemberName] string symbol = "")
        => metadata.Metadata = metadata.Metadata.Set(symbol, value);
}
