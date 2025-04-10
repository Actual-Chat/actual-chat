using System.ComponentModel;
using ActualChat.Internal;
using ActualLab.Fusion.Blazor;
using ActualLab.Generators;

namespace ActualChat;

#pragma warning disable CS0659, CS0660, CS0661 // Type overrides Object.Equals(object o) but does not override Object.GetHashCode()

[DataContract]
[JsonConverter(typeof(StringIdentifierJsonConverter<NodeRef2>))]
[Newtonsoft.Json.JsonConverter(typeof(StringIdentifierNewtonsoftJsonConverter<NodeRef2>))]
[TypeConverter(typeof(StringIdentifierTypeConverter<NodeRef2>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public sealed class NodeRef2 : StringIdentifier, IStringIdentifier<NodeRef2>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<NodeRef2>();
    private static readonly ILruCache<string, NodeRef2> Cache = CreateCache<NodeRef2>(64);
    private static readonly RandomStringGenerator IdGenerator = new(8, Alphabet.AlphaNumeric);

    public static readonly NodeRef2 OwnNodeAlias = new("@");

    // Factories and constructors

    public static NodeRef2 New()
        => new(IdGenerator.Next());

    private NodeRef2(string value) : base(value)
    { }

    // Equality

    public bool Equals(NodeRef2? other)
        => !ReferenceEquals(other, null)
            && HashCode == other.HashCode
            && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj)
        => obj is NodeRef2 other && Equals(other);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(NodeRef2? left, NodeRef2? right)
        => left?.Equals(right) ?? right is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(NodeRef2? left, NodeRef2? right)
        => !(left?.Equals(right) ?? right is null);

    // Format & Parse

    public static NodeRef2 Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<NodeRef2>(s);

    public static bool TryParse(string? s, [NotNullWhen(true)] out NodeRef2? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        if (Cache.TryGetValue(s, out var cached)) {
            result = cached;
            return true;
        }

        if (s.Length is < 6 or > 32 || !Alphabet.AlphaNumericDash.IsMatch(s))
            return false;

        result = new NodeRef2(s);
        result = Cache.AddOrGet(s, result);
        return true;
    }
}
