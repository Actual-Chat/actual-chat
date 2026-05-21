using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Reference to a mentioned entity in chat content. The value is <c>prefix:</c>
/// where the prefix selects a <see cref="MentionKind"/> and the local id parses into an
/// <see cref="IMentionTargetId"/>. See <see cref="MentionKind.ByPrefix"/> for the registered kinds.
/// </summary>
// TODO(AY): Rename to MentionRef
[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringLikeJsonConverter<MentionId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<MentionId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<MentionId>))]
[TypeConverter(typeof(StringLikeTypeConverter<MentionId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class MentionId : StringIdentifier, IStringIdentifier<MentionId>, IHasShardKey<string>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<MentionId>();
    private static readonly ILruCache<string, MentionId> Cache = CreateCache<MentionId>(256);

    [IgnoreDataMember]
    public MentionKind Kind { get; }
    [IgnoreDataMember]
    public IMentionTargetId TargetId { get; }
    [IgnoreDataMember]
    public string ShardKey => TargetId.ShardKey;

    // Factories and constructors

    public static MentionId NewAuthor(AuthorId authorId)
        => Create(MentionKind.Author, authorId);
    public static MentionId NewUser(UserId userId)
        => Create(MentionKind.User, userId);
    public static MentionId NewChat(ChatId chatId)
        => Create(MentionKind.Chat, chatId);
    public static MentionId NewPlace(PlaceId placeId)
        => Create(MentionKind.Place, placeId);
    public static MentionId NewEmoji(EmojiRef emojiRef)
        => Create(MentionKind.Emoji, emojiRef);
    public static MentionId NewGif(GifRef gifRef)
        => Create(MentionKind.Gif, gifRef);

    private static MentionId Create(MentionKind kind, IMentionTargetId targetId)
    {
        var value = Format(kind, targetId.Value);
        if (Cache.TryGetValue(value, out var cached))
            return cached;
        return Cache.AddOrGet(value, new MentionId(value, kind, targetId));
    }

    private MentionId(string value, MentionKind kind, IMentionTargetId targetId) : base(value)
    {
        Kind = kind;
        TargetId = targetId;
    }

    // Equality

    public bool Equals(MentionId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is MentionId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(MentionId? left, MentionId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(MentionId? left, MentionId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    private static string Format(MentionKind kind, string localId)
        => $"{kind.Prefix}:{localId}";

    public static MentionId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<MentionId>(s);

    public static MentionId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static MentionId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out MentionId? result)
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

        result = new MentionId(s, kind, target);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
