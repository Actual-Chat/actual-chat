using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<MentionId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<MentionId2>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<MentionId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<MentionId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class MentionId2 : StringIdentifier, IStringIdentifier<MentionId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<MentionId2>();
    private static readonly ILruCache<string, MentionId2> Cache = CreateCache<MentionId2>(256);

    [IgnoreDataMember]
    public PrincipalId2 PrincipalId { get; }
    [IgnoreDataMember]
    public PrincipalKind Kind => PrincipalId.Kind;

    // Factories and constructors

    public static MentionId2 NewAuthor(AuthorId2 authorId)
        => new(Format("a", authorId.Value), authorId);

    public static MentionId2 NewUser(UserId2 userId)
        => new(Format("u", userId.Value), userId);

    private MentionId2(string value, PrincipalId2 principalId) : base(value)
        => PrincipalId = principalId;

    // Equality

    public bool Equals(MentionId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is MentionId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(MentionId2? left, MentionId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(MentionId2? left, MentionId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    private static string Format(string prefix, string id)
        => $"{prefix}:{id}";

    public static MentionId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<MentionId2>(s);

    public static MentionId2? TryParse(string? s)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out MentionId2? result)
    {
        result = null;
        if (s.IsNullOrEmpty() || s.Length < 2)
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.OrdinalStartsWith("a:")) {
            if (!AuthorId2.TryParse(s[2..], out var authorId))
                return false;
            result = NewAuthor(authorId);
        }
        else if (s.OrdinalStartsWith("u:")) {
            if (!UserId2.TryParse(s[2..], out var userId))
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
