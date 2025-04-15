using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()
#pragma warning disable MA0097 // IComparable should implement <, >, etc.

[DataContract, MemoryPackable(GenerateType.NoGenerate)]
[JsonConverter(typeof(StringIdentifierJsonConverter<StreamId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<StreamId>))]
[MessagePackFormatter(typeof(StringIdentifierMessagePackFormatter<StreamId>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<StreamId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial class StreamId : StringIdentifier, IStringIdentifier<StreamId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<StreamId>();
    private static readonly ILruCache<string, StreamId> Cache = CreateCache<StreamId>(32);

    public static Func<string> LocalIdGenerator { get; } = () => Ulid.NewUlid().ToString();
    public const char Delimiter = '-';

    [IgnoreDataMember]
    public NodeRef NodeRef { get; }
    [IgnoreDataMember]
    public string LocalId { get; }

    // Factories and constructors

    public static StreamId New(NodeRef nodeRef)
    {
        var localId = LocalIdGenerator.Invoke();
        return new(Format(nodeRef, localId), nodeRef, localId);
    }

    public static StreamId New(NodeRef nodeRef, string localId)
        => new(Format(nodeRef, localId), nodeRef, localId);

    private StreamId(string value, NodeRef nodeRef, string localId) : base(value)
    {
        NodeRef = nodeRef;
        LocalId = localId;
    }

    // Equality

    public bool Equals(StreamId? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is StreamId other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(StreamId? left, StreamId? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(StreamId? left, StreamId? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(NodeRef nodeRef, string localId)
        => $"{nodeRef}{Delimiter}{localId}";

    public static StreamId Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<StreamId>(s);

    public static StreamId? ParseOrNull(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static StreamId? TryParse(string? s)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out StreamId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        var parts = s.Split(Delimiter, 2);
        if (parts.Length != 2)
            return false;

        if (!NodeRef.TryParse(parts[0], out var nodeRef))
            return false;

        var localId = parts[1];
        if (localId.IsNullOrEmpty() || !Alphabet.AlphaNumericDash.IsMatch(localId))
            return false;

        result = new StreamId(s, nodeRef, localId);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
