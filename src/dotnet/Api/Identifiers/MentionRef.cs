using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Reference to a mentioned entity in chat content. The value is <c>prefix:</c>
/// where the prefix selects a <see cref="MentionKind"/> and the local id parses into an
/// <see cref="IMentionTarget"/>. See <see cref="MentionKind.ByPrefix"/> for the registered kinds.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<MentionRef>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<MentionRef>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<MentionRef>))]
[TypeConverter(typeof(StringLikeTypeConverter<MentionRef>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class MentionRef : StringIdentifier, IStringIdentifier<MentionRef>, IHasShardKey<string>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<MentionRef>();
    private static readonly ILruCache<string, MentionRef> Cache = CreateCache<MentionRef>(256);

    [IgnoreDataMember]
    public MentionKind Kind { get; }
    [IgnoreDataMember]
    public IMentionTarget Target { get; }
    [IgnoreDataMember]
    public string ShardKey => Target.ShardKey;

    // Factories and constructors

    public static MentionRef NewAuthor(AuthorId authorId)
        => Create(MentionKind.Author, authorId);
    public static MentionRef NewUser(UserId userId)
        => Create(MentionKind.User, userId);
    public static MentionRef NewChat(ChatId chatId)
        => Create(MentionKind.Chat, chatId);
    public static MentionRef NewPlace(PlaceId placeId)
        => Create(MentionKind.Place, placeId);
    public static MentionRef NewEmoji(EmojiRef emojiRef)
        => Create(MentionKind.Emoji, emojiRef);

    private static MentionRef Create(MentionKind kind, IMentionTarget target)
    {
        var value = Format(kind, target.Value);
        if (Cache.TryGetValue(value, out var cached))
            return cached;
        return Cache.AddOrGet(value, new MentionRef(value, kind, target));
    }

    private MentionRef(string value, MentionKind kind, IMentionTarget target) : base(value)
    {
        Kind = kind;
        Target = target;
    }

    // Equality

    public bool Equals(MentionRef? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is MentionRef other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(MentionRef? left, MentionRef? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(MentionRef? left, MentionRef? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    private static string Format(MentionKind kind, string localId)
        => $"{kind.Prefix}:{localId}";

    public static MentionRef Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<MentionRef>(s);

    public static MentionRef? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static MentionRef? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out MentionRef? result)
    {
        result = null;
        if (s.IsNullOrEmpty() || s.Length < 3)
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var colon = s.IndexOf(':');
        if (colon <= 0 || colon == s.Length - 1)
            return false;

        var prefix = s[..colon];
        if (!MentionKind.ByPrefix.TryGetValue(prefix, out var kind))
            return false;

        var localId = s[(colon + 1)..];
        if (!kind.TryParseTarget(localId, out var target))
            return false;

        result = new MentionRef(s, kind, target);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
