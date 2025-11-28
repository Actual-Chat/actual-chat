using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<UploadId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<UploadId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<UploadId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<UploadId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class UploadId : StringIdentifier, IStringIdentifier<UploadId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<UploadId>();
    private static readonly ILruCache<string, UploadId> Cache = CreateCache<UploadId>(32);

    public static readonly RandomStringGenerator IdGenerator = new(20, Alphabet.AlphaNumeric);

    // Factories and constructors

    public static UploadId New()
        => new(IdGenerator.Next());

    private UploadId(string value) : base(value)
    { }

    // Equality

    public bool Equals(UploadId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is UploadId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(UploadId? left, UploadId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(UploadId? left, UploadId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static UploadId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<UploadId>(s);

    public static UploadId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static UploadId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out UploadId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.Length is < 20 or > 30)
            return false;

        result = new UploadId(s);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
