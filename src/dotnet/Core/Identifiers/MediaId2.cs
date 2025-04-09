using System.ComponentModel;
using System.Text;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;
using ActualChat.Hashing;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<MediaId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<MediaId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<MediaId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class MediaId2(string value, string scope, string localId, AssumeValid _)
    : StringIdentifier(value), IStringIdentifier<MediaId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<MediaId2>();
    private static readonly ILruCache<string, MediaId2> Cache = CreateCache<MediaId2>(256);
    private static readonly RandomStringGenerator IdGenerator = new(10, Alphabet.AlphaNumeric);
    public const char Delimiter = ':';

    [IgnoreDataMember]
    public string Scope { get; } = scope;
    [IgnoreDataMember]
    public string LocalId { get; } = localId;

    [IgnoreDataMember] [field: AllowNull, MaybeNull]
    private string SecureHash
        => field ??= Value.Hash(Encoding.UTF8).SHA256().AlphaNumeric();

    public static MediaId2 New(string scope)
    {
        var localId = IdGenerator.Next();
        return new MediaId2(Format(scope, localId), scope, localId, AssumeValid.Option);
    }

    public string GetContentId(string fileExt)
        => $"media/{SecureHash}/{LocalId}{fileExt}";

    // Equality

    public bool Equals(MediaId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is MediaId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(MediaId2? left, MediaId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(MediaId2? left, MediaId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Parsing

    private static string Format(string scope, string localId)
        => $"{scope}{Delimiter}{localId}";

    public static MediaId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<MediaId2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out MediaId2? result)
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

        result = new MediaId2(s, scope, localId, AssumeValid.Option);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
