using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

/// <summary>
/// Base identifier for principals (users or authors).
/// </summary>
[DataContract]
[JsonConverter(typeof(StringLikeJsonConverter<PrincipalId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<PrincipalId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<PrincipalId>))]
[TypeConverter(typeof(StringLikeTypeConverter<PrincipalId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public partial class PrincipalId : StringIdentifier, IStringIdentifier<PrincipalId>, IHasShardKey<string>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<PrincipalId>();

    [IgnoreDataMember]
    public PrincipalKind Kind { get; }
    [IgnoreDataMember]
    public virtual string ShardKey => Value;

    protected PrincipalId(string value, PrincipalKind kind) : base(value)
        => Kind = kind;

    // Equality

    public bool Equals(PrincipalId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value);
    public override bool Equals(object? obj)
        => obj is PrincipalId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(PrincipalId? left, PrincipalId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(PrincipalId? left, PrincipalId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static PrincipalId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PrincipalId>(s);

    public static PrincipalId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static PrincipalId? TryParse(string? s, bool allowNull = false)
        => allowNull && s.IsNullOrEmpty() ? null
            : !TryParse(s, out var result) ? null
            : result;

    public static bool TryParse(string? s, [NotNullWhen(true)] out PrincipalId? result)
    {
        if (AuthorId.TryParse(s, out var authorId)) {
            result = authorId;
            return true;
        }
        if (UserId.TryParse(s, out var userId)) {
            result = userId;
            return true;
        }

        result = null;
        return false;
    }
}
