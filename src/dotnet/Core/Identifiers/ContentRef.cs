using System.ComponentModel;
using ActualChat.Internal;

namespace ActualChat;

[DataContract, MessagePackObject]
[JsonConverter(typeof(StringLikeJsonConverter<ContentRef>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<ContentRef>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<ContentRef>))]
[TypeConverter(typeof(StringLikeTypeConverter<ContentRef>))]
public sealed class ContentRef : StringIdentifier, IStringIdentifier<ContentRef>
{
    private static readonly Lock Lock = new();
    private static readonly Dictionary<Type, string> Prefixes = new();
    private static readonly Dictionary<string, Func<string, ContentId?>> Parsers = new();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ContentId ContentId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public override ShardKey ShardKey => ContentId.ShardKey;

    public static void Register<TId>(string prefix)
        where TId : ContentId, IStringIdentifier<TId>
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        if (prefix.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '~')))
            throw new ArgumentOutOfRangeException(nameof(prefix));

        lock (Lock) {
            if (Prefixes.ContainsKey(typeof(TId)) || Parsers.ContainsKey(prefix))
                throw StandardError.Constraint("Content ID type and prefix must be registered only once.");

            Prefixes.Add(typeof(TId), prefix);
            Parsers.Add(prefix, static s => TId.TryParse(s));
        }
    }

    public ContentRef(ContentId contentId)
        : base(Format(contentId))
        => ContentId = contentId;

    public bool Equals(ContentRef? other)
        => other is not null && HashCode == other.HashCode && string.Equals(Value, other.Value);

    public override bool Equals(object? obj)
        => obj is ContentRef other && Equals(other);

    public override int GetHashCode()
        => HashCode;

    public static bool operator ==(ContentRef? left, ContentRef? right)
        => left?.Equals(right) ?? right is null;

    public static bool operator !=(ContentRef? left, ContentRef? right)
        => !(left == right);

    public static ContentRef Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ContentRef>(s);

    public static ContentRef? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static ContentRef? TryParse(string? s, bool allowNull = false)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out ContentRef? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        var separator = s.IndexOf(':');
        if (separator <= 0)
            return false;

        Func<string, ContentId?>? parser;
        lock (Lock)
            parser = Parsers.GetValueOrDefault(s[..separator]);

        var contentId = parser?.Invoke(s[(separator + 1)..]);
        if (contentId is null)
            return false;

        result = contentId.ContentRef;
        return true;
    }

    // Private methods

    private static string Format(ContentId contentId)
    {
        ArgumentNullException.ThrowIfNull(contentId);
        return $"{GetPrefix(contentId.GetType())}:{contentId.Value}";
    }

    private static string GetPrefix(Type type)
    {
        lock (Lock)
            for (var current = type; current is not null; current = current.BaseType)
                if (Prefixes.TryGetValue(current, out var prefix))
                    return prefix;

        throw StandardError.NotSupported($"No content ID prefix is registered for '{type.GetName()}'.");
    }
}
