using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;

namespace ActualChat;

/// <summary>
/// Identifier of a web hook.
/// </summary>
#pragma warning disable CS0659, CS0660, CS0661 // Overrides ==/Equals but not GetHashCode (provided by base)
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<WebHookId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<WebHookId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<WebHookId>))]
[TypeConverter(typeof(StringLikeTypeConverter<WebHookId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class WebHookId : ContentId, IStringIdentifier<WebHookId>
{
    private static readonly ILruCache<string, WebHookId> Cache = CreateCache<WebHookId>(128);
    private static readonly RandomStringGenerator IdGenerator = new(16, Alphabet.AlphaNumeric);

    // Factories and constructors

    public static WebHookId New()
        => new (IdGenerator.Next());

    private WebHookId(string value) : base(value) { }

    // Equality

    public bool Equals(WebHookId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is WebHookId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(WebHookId? left, WebHookId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(WebHookId? left, WebHookId? right)
        => !(left?.Equals(right) ?? right is null);

    // Parsing

    public static WebHookId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<WebHookId>(s);

    public static WebHookId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static WebHookId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out WebHookId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.Length > 64 || !Alphabet.AlphaNumeric.IsMatch(s))
            return false;

        result = new WebHookId(s);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
