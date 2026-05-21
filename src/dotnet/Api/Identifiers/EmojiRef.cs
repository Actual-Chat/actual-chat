using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Reference to a unicode-emoji slug or a custom emoji id used as a <see cref="MentionId"/> target.
/// </summary>
[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringLikeJsonConverter<EmojiRef>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<EmojiRef>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<EmojiRef>))]
[TypeConverter(typeof(StringLikeTypeConverter<EmojiRef>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class EmojiRef : StringIdentifier, IStringIdentifier<EmojiRef>, IMentionTargetId
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<EmojiRef>();
    private static readonly ILruCache<string, EmojiRef> Cache = CreateCache<EmojiRef>(128);

    [IgnoreDataMember]
    public string Text => field ??= Value.UrlDecode();
    [IgnoreDataMember]
    public MentionKind MentionKind => MentionKind.Emoji;
    [IgnoreDataMember]
    public string ShardKey => Value;

    // Factories and constructors

    public static EmojiRef New(Emoji emoji)
        => NewFromText(emoji.Id.Value);

    public static EmojiRef NewFromText(string text)
        => Parse(text.UrlEncode());

    private EmojiRef(string value) : base(value)
    { }

    // Equality

    public bool Equals(EmojiRef? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is EmojiRef other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(EmojiRef? left, EmojiRef? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(EmojiRef? left, EmojiRef? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static EmojiRef Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<EmojiRef>(s);

    public static EmojiRef? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static EmojiRef? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out EmojiRef? result)
    {
        result = null;
        if (s.IsNullOrEmpty() || s.Length > 64)
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (!IsValidSlug(s))
            return false;

        result = new EmojiRef(s);
        result = Cache.AddOrGet(s, result);
        return true;
    }

    // Private methods

    private static bool IsValidSlug(string s)
    {
        foreach (var c in s) {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':' or '%' or '~'))
                return false;
        }
        return true;
    }
}
