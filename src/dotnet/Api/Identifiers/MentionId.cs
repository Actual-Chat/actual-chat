using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

// TODO(AY): Rename to MentionRef
[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<MentionId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<MentionId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<MentionId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<MentionId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class MentionId : StringIdentifier, IStringIdentifier<MentionId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<MentionId>();
    private static readonly ILruCache<string, MentionId> Cache = CreateCache<MentionId>(256);

    [IgnoreDataMember]
    public PrincipalId PrincipalId { get; }
    [IgnoreDataMember]
    public PrincipalKind Kind => PrincipalId.Kind;

    // Factories and constructors

    public static MentionId NewAuthor(AuthorId authorId)
        => new(Format("a", authorId.Value), authorId);

    public static MentionId NewUser(UserId userId)
        => new(Format("u", userId.Value), userId);

    private MentionId(string value, PrincipalId principalId) : base(value)
        => PrincipalId = principalId;

    // Equality

    public bool Equals(MentionId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is MentionId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(MentionId? left, MentionId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(MentionId? left, MentionId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    private static string Format(string prefix, string id)
        => $"{prefix}:{id}";

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
        if (s.IsNullOrEmpty() || s.Length < 2)
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.OrdinalStartsWith("a:")) {
            if (!AuthorId.TryParse(s[2..], out var authorId))
                return false;
            result = NewAuthor(authorId);
        }
        else if (s.OrdinalStartsWith("u:")) {
            if (!UserId.TryParse(s[2..], out var userId))
                return false;
            result = NewUser(userId);
        }
        else {
            return false;
        }

        result = Cache.AddOrGet(s, result);
        return true;
    }
}
