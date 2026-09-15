using System.ComponentModel;
using ActualChat.Internal;

namespace ActualChat;

[DataContract, MessagePackObject]
[JsonConverter(typeof(StringLikeJsonConverter<TypedObjectId>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<TypedObjectId>))]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<TypedObjectId>))]
[TypeConverter(typeof(StringLikeTypeConverter<TypedObjectId>))]
public sealed class TypedObjectId : IObjectId<TypedObjectId>
{
    private static readonly Lock Lock = new();
    private static readonly Dictionary<Type, string> Prefixes = new();
    private static readonly Dictionary<string, Func<string, ObjectId?>> Parsers = new();

    [DataMember(Order = 0)]
    public readonly string Value;
    [IgnoreDataMember]
    public readonly int HashCode;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Symbol Id => new(Value, HashCode);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ObjectId ObjectId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public PartitionKey PartitionKey => ObjectId.PartitionKey;

    string IHasId<string>.Id => Value;
    string IStringLike.Value => Value;
    int IObjectId.HashCode => HashCode;

    public static void Register<TId>(string prefix)
        where TId : ObjectId, IObjectId<TId>
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        if (prefix.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new ArgumentOutOfRangeException(nameof(prefix));

        lock (Lock) {
            if (Prefixes.ContainsKey(typeof(TId)) || Parsers.ContainsKey(prefix))
                throw StandardError.Constraint("Object ID type and prefix must be registered only once.");

            Prefixes.Add(typeof(TId), prefix);
            Parsers.Add(prefix, static s => TId.TryParse(s));
        }
    }

    public TypedObjectId(ObjectId objectId)
    {
        ArgumentNullException.ThrowIfNull(objectId);
        ObjectId = objectId;
        Value = $"{GetPrefix(objectId.GetType())}:{objectId.Value}";
        HashCode = Value.GetHashCode();
    }

    public override string ToString()
        => Value;

    public bool Equals(TypedObjectId? other)
        => other is not null && HashCode == other.HashCode && string.Equals(Value, other.Value);

    public override bool Equals(object? obj)
        => obj is TypedObjectId other && Equals(other);

    public override int GetHashCode()
        => HashCode;

    public static bool operator ==(TypedObjectId? left, TypedObjectId? right)
        => left?.Equals(right) ?? right is null;

    public static bool operator !=(TypedObjectId? left, TypedObjectId? right)
        => !(left == right);

    public static TypedObjectId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<TypedObjectId>(s);

    public static TypedObjectId? ParseNullable(string? s)
        => s.IsNullOrEmpty() ? null : Parse(s);

    public static TypedObjectId? TryParse(string? s, bool allowNull = false)
        => TryParse(s, out var result) ? result : null;

    public static bool TryParse(string? s, [NotNullWhen(true)] out TypedObjectId? result)
    {
        result = null;
        if (s.IsNullOrEmpty())
            return false;

        var separator = s.IndexOf(':');
        if (separator <= 0)
            return false;

        Func<string, ObjectId?>? parser;
        lock (Lock)
            parser = Parsers.GetValueOrDefault(s[..separator]);

        var objectId = parser?.Invoke(s[(separator + 1)..]);
        if (objectId is null)
            return false;

        result = objectId.TypedId;
        return true;
    }

    // Private methods

    private static string GetPrefix(Type type)
    {
        lock (Lock)
            for (var current = type; current is not null; current = current.BaseType)
                if (Prefixes.TryGetValue(current, out var prefix))
                    return prefix;

        throw StandardError.NotSupported($"No object ID prefix is registered for '{type.GetName()}'.");
    }
}
