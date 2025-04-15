using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<PrincipalId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<PrincipalId2>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<PrincipalId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<PrincipalId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public partial class PrincipalId2 : StringIdentifier, IStringIdentifier<PrincipalId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<PrincipalId2>();

    [IgnoreDataMember]
    public PrincipalKind Kind { get; }

    protected PrincipalId2(string value, PrincipalKind kind) : base(value)
        => Kind = kind;

    // Equality

    public bool Equals(PrincipalId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is PrincipalId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(PrincipalId2? left, PrincipalId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(PrincipalId2? left, PrincipalId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static PrincipalId2 Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PrincipalId2>(s);

    public static PrincipalId2? TryParse(string? s)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out PrincipalId2? result)
    {
        if (AuthorId2.TryParse(s, out var authorId)) {
            result = authorId;
            return true;
        }
        if (UserId2.TryParse(s, out var userId)) {
            result = userId;
            return true;
        }

        result = null;
        return false;
    }
}
