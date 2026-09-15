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

    [IgnoreDataMember]
    public string Symbol { get; }

    [IgnoreDataMember]
    public string Title { get; }

    [IgnoreDataMember]
    public EmojiGroup Group { get; }

    // Factories and constructors

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
        => ReferenceEquals(this, other); // Fine for Emoji
    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj); // Fine for Emoji

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Emoji? left, Emoji? right)
        => ReferenceEquals(left, right); // Fine for Emoji

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Emoji? left, Emoji? right)
        => !ReferenceEquals(left, right); // Fine for Emoji

    // Parsing

    public static Emoji Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<Emoji>(s);

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
}
