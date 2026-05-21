using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Reference to a picker-selected GIF used as a <see cref="MentionId"/> target.
/// The value is a URL-encoded picker id resolved to media metadata at render time.
/// </summary>
[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringLikeJsonConverter<GifRef>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<GifRef>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<GifRef>))]
[TypeConverter(typeof(StringLikeTypeConverter<GifRef>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class GifRef : StringIdentifier, IStringIdentifier<GifRef>, IMentionTargetId
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<GifRef>();
    private static readonly ILruCache<string, GifRef> Cache = CreateCache<GifRef>(128);

    public const int MaxLength = 1024;

    [IgnoreDataMember]
    public MentionKind MentionKind => MentionKind.Gif;
    [IgnoreDataMember]
    public string ShardKey => Value;

    // Factories and constructors

    private GifRef(string value) : base(value)
    { }

    // Equality

    public bool Equals(GifRef? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is GifRef other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(GifRef? left, GifRef? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(GifRef? left, GifRef? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static GifRef Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<GifRef>(s);

    public static GifRef? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static GifRef? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out GifRef? result)
    {
        result = null;
        if (s.IsNullOrEmpty() || s.Length > MaxLength)
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (!IsValidEncodedId(s))
            return false;

        result = new GifRef(s);
        result = Cache.AddOrGet(s, result);
        return true;
    }

    // Private methods

    private static bool IsValidEncodedId(string s)
    {
        foreach (var c in s) {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':' or '%' or '~'))
                return false;
        }
        return true;
    }
}
