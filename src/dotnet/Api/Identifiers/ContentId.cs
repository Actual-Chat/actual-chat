using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Unique identifier for content that can be associated with reactions or other metadata.
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<ContentId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<ContentId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<ContentId>))]
[TypeConverter(typeof(StringLikeTypeConverter<ContentId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public partial class ContentId : StringIdentifier, IStringIdentifier<ContentId>
{
    public const string Delimiter = ":";
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ChatEntryId>();
    private static readonly ILruCache<string, ContentId> Cache = CreateCache<ContentId>(256);

    [IgnoreDataMember] public ContentKind Kind { get; }
    [IgnoreDataMember] public StringIdentifier TargetId { get; }

    private ContentId(string value, ContentKind kind, StringIdentifier targetId)
     : base(value)
    {
        Kind = kind;
        TargetId = targetId;
    }

    public static ContentId New(StringIdentifier id)
    {
        var kind = GetKind(id);
        if (kind is null)
            throw new ArgumentOutOfRangeException(nameof(id));
        return new ContentId(Format(kind.Value, id), kind.Value, id);
    }

    public static bool IsValid(StringIdentifier id)
        => GetKind(id) is not null;

    private static ContentKind? GetKind(StringIdentifier id)
    {
        if (id is ChatId)
            return ContentKind.Chat;
        if (id is UserId)
            return ContentKind.User;
        if (id is ChatEntryId)
            return ContentKind.ChatEntry;
        if (id is AuthorId)
            return ContentKind.Author;
        if (id is PlaceId)
            return ContentKind.Place;
        return null;
    }

    // Equality

    public bool Equals(ContentId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is ContentId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ContentId? left, ContentId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ContentId? left, ContentId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(ContentKind kind, StringIdentifier targetId)
        => $"{(int)kind}{Delimiter}{targetId.Value}";

    public static ContentId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ContentId>(s);

    public static ContentId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static ContentId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out ContentId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var kindLength = s.IndexOf(Delimiter);
        if (kindLength < 0)
            return false;

        if (!int.TryParse(s[..kindLength], out var iKind))
            return false;

        var kind = (ContentKind)iKind;
        if (!Enum.IsDefined(kind))
            return false;

        var sTargetId = s[(kindLength + 1)..];
        if (sTargetId.IsNullOrEmpty())
            return false;

        switch (kind) {
            case ContentKind.User:
                if (!UserId.TryParse(sTargetId, out var userId))
                    return false;
                result = new ContentId(s, kind, userId);
                return true;
            case ContentKind.Chat:
                if (!ChatId.TryParse(sTargetId, out var chatId))
                    return false;
                result = new ContentId(s, kind, chatId);
                return true;
            case ContentKind.ChatEntry:
                if (!ChatEntryId.TryParse(sTargetId, out var chatEntryId))
                    return false;
                result = new ContentId(s, kind, chatEntryId);
                return true;
            case ContentKind.Author:
                if (!AuthorId.TryParse(sTargetId, out var authorId))
                    return false;
                result = new ContentId(s, kind, authorId);
                return true;
            case ContentKind.Place:
                if (!PlaceId.TryParse(sTargetId, out var placeId))
                    return false;
                result = new ContentId(s, kind, placeId);
                return true;
        }

        result = null;
        return false;
    }
}

/// <summary>
/// Specifies the type of content a <see cref="ContentId"/> refers to.
/// </summary>
public enum ContentKind
{
    User = 0,
    Chat = 1,
    ChatEntry = 2,
    Author = 3,
    Place = 4
}
