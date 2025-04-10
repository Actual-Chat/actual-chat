using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<StreamId2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<StreamId2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<StreamId2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class StreamId2 : StringIdentifier, IStringIdentifier<StreamId2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<StreamId2>();
    private static readonly ILruCache<string, StreamId2> Cache = CreateCache<StreamId2>(32);

    public static Func<string> LocalIdGenerator { get; } = () => Ulid.NewUlid().ToString();
    public const char Delimiter = '-';

    [IgnoreDataMember]
    public NodeRef NodeRef { get; }
    [IgnoreDataMember]
    public string LocalId { get; }

    // Factories and constructors

    public static StreamId2 New(NodeRef nodeRef)
    {
        var localId = LocalIdGenerator.Invoke();
        return new(Format(nodeRef, localId), nodeRef, localId);
    }

    private StreamId2(string value, NodeRef nodeRef, string localId) : base(value)
    {
        NodeRef = nodeRef;
        LocalId = localId;
    }

    // Equality

    public bool Equals(StreamId2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is StreamId2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(StreamId2? left, StreamId2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(StreamId2? left, StreamId2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static string Format(NodeRef nodeRef, string localId)
        => $"{nodeRef}{Delimiter}{localId}";

    public static StreamId2 Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<StreamId2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out StreamId2? result)
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

        result = new StreamId2(s, nodeRef, localId);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
