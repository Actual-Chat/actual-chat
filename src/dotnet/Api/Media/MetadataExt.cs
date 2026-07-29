namespace ActualChat.Media;

/// <summary>
/// Interface for types that have a metadata property bag.
/// </summary>
public interface IHasMetadata
{
    MetadataBag Metadata { get; init; }
}

internal static class MetadataExt
{
    private static Action<IHasMetadata, MetadataBag> MetadataSetter
        => field ??= typeof(IHasMetadata).GetProperty("Metadata")!.GetSetter<IHasMetadata, MetadataBag>();

    public static T GetMetadataValue<T>(this IHasMetadata source, T @default = default!, [CallerMemberName] string symbol = "") {
        var value = source.Metadata[symbol];
        if (value == null)
            return @default;

        // TODO(AY): remove this workaround when int is not deserialized as long
        if (typeof(T) == typeof(int) && value is not int)
            value = Convert.ToInt32(value);

        // TODO(AY): remove this workaround once we touch all Media
        if (typeof(T) == typeof(Moment) && value is not Moment) {
            // Handle legacy data stored as EpochOffsetTicks (long) or ISO8601 string instead of Moment
            value = value switch {
                long ticks => new Moment(ticks),
                string s => Moment.Parse(s),
                _ => throw new InvalidCastException($"Cannot convert {value.GetType().Name} to Moment."),
            };
        }

        return (T)value;
    }

    public static void SetMetadataValue<T>(this IHasMetadata target, T value, [CallerMemberName] string symbol = "")
        => MetadataSetter.Invoke(target, target.Metadata.Set(symbol, value));
}
