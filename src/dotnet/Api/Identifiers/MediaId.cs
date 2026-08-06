using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Unique identifier for uploaded media content.
/// </summary>
[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringLikeJsonConverter<MediaId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<MediaId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<MediaId>))]
[TypeConverter(typeof(StringLikeTypeConverter<MediaId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class MediaId : StringIdentifier, IStringIdentifier<MediaId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<MediaId>();
    private static readonly ILruCache<string, MediaId> Cache = CreateCache<MediaId>(128);
    private static readonly RandomStringGenerator IdGenerator = new(10, Alphabet.AlphaNumeric);
    private static readonly RandomStringGenerator ScopeGenerator = new(16, Alphabet.AlphaNumeric);

    public const char Delimiter = ':';

    [IgnoreDataMember]
    public string Scope { get; }
    [IgnoreDataMember]
    public string LocalId { get; }

    // Factories and constructors

    public static MediaId New(string scope)
    {
        var localId = IdGenerator.Next();
        return new MediaId(Format(scope, localId), scope, localId);
    }

    public static MediaId New(string scope, string localId)
        => new (Format(scope, localId), scope, localId);

    public static string NewScope()
        => ScopeGenerator.Next();

    private MediaId(string value, string scope, string localId) : base(value)
    {
        Scope = scope;
        LocalId = localId;
    }

    // Equality

    public bool Equals(MediaId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is MediaId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(MediaId? left, MediaId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(MediaId? left, MediaId? right)
        => !(left?.Equals(right) ?? right is null);

    // Parsing

    private static string Format(string scope, string localId)
        => $"{scope}{Delimiter}{localId}";

    public static MediaId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<MediaId>(s);

    public static MediaId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static MediaId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out MediaId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.Length > 2048)
            return false;

        var parts = s.Split(Delimiter);
        if (parts.Length != 2)
            return false;

        var scope = parts[0];
        if (!Alphabet.AlphaNumericDash.IsMatch(scope))
            return false;

        var localId = parts[1];
        if (!Alphabet.AlphaNumeric.IsMatch(localId))
            return false;

        result = new MediaId(s, scope, localId);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
