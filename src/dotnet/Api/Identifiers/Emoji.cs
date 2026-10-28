using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Represents an emoji with its codepoint and title.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<Emoji>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<Emoji>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<Emoji>))]
[TypeConverter(typeof(StringLikeTypeConverter<Emoji>))]
[ParameterComparer(typeof(ByRefParameterComparer))] // Fine for Emoji
public sealed partial class Emoji : StringIdentifier, IStringIdentifier<Emoji>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<Emoji>();
    private static readonly ILruCache<string, Emoji> UnsupportedCache = CreateCache<Emoji>(32);

    [IgnoreDataMember]
    public string Symbol { get; }

    [IgnoreDataMember]
    public string Title { get; }

    [IgnoreDataMember]
    public EmojiGroup Group { get; }

    // Factories and constructors

    private static Emoji NewUnsupported(string id)
        => new(id, id);

    // Standard emoji: id == symbol
    internal Emoji(string symbol, string title, EmojiGroup group = EmojiGroup.Gestures) : base(symbol)
    {
        Symbol = symbol;
        Title = title;
        Group = group;
    }

    // Custom variant: separate id
    internal Emoji(string symbol, string id, string title, EmojiGroup group = EmojiGroup.Gestures) : base(id)
    {
        Symbol = symbol;
        Title = title;
        Group = group;
    }

    // Equality

    public bool Equals(Emoji? other)
        // By value, since an unsupported emoji isn't a singleton
        => ReferenceEquals(this, other)
            || (!ReferenceEquals(other, null) && HashCode == other.HashCode && string.Equals(Value, other.Value));
    public override bool Equals(object? obj)
        => obj is Emoji other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Emoji? left, Emoji? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Emoji? left, Emoji? right)
        => !(left?.Equals(right) ?? right is null);

    // Parsing

    public static Emoji Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<Emoji>(s);

    static Emoji IStringLike<Emoji>.Parse(string? s)
        => ParseOrUnsupported(s);

    public static Emoji ParseOrUnsupported(string? s)
        // Formatters deserialize through this one, so an emoji added by a later release reads as
        // a placeholder rather than failing the whole payload; Parse itself stays strict.
        => TryParse(s, out var result) ? result
            : IsValidUnsupportedId(s) ? GetUnsupported(s)
            : throw StandardError.Format<Emoji>(s);

    public static Emoji? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static Emoji? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out Emoji? result)
    {
        if (!s.IsNullOrEmpty() && Emojis.ById.TryGetValue(s, out result))
            return true;

        result = null;
        return false;
    }

    public Emoji RequireSupported()
        => Emojis.ById.ContainsKey(Value) ? this : throw StandardError.Format<Emoji>(Value);

    // Private methods

    private static Emoji GetUnsupported(string id)
        => UnsupportedCache.TryGetValue(id, out var cached) ? cached
            : UnsupportedCache.AddOrGet(id, NewUnsupported(id));

    private static bool IsValidUnsupportedId([NotNullWhen(true)] string? s)
        => !s.IsNullOrEmpty()
            && s.Length <= 32
            && !s.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));
}
