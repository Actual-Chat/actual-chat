using System.ComponentModel;
using ActualChat.Internal;

namespace ActualChat.Users;

[StructLayout(LayoutKind.Auto)]
[DataContract]
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptOut)]
[MessagePackFormatter(typeof(StringLikeMessagePackFormatter<UserIdentity>))]
[JsonConverter(typeof(StringLikeJsonConverter<UserIdentity>))]
[Newtonsoft.Json.JsonConverter(typeof(StringLikeNewtonsoftJsonConverter<UserIdentity>))]
[TypeConverter(typeof(StringLikeTypeConverter<UserIdentity>))]
public readonly partial record struct UserIdentity : IStringLike<UserIdentity>, IComparable<UserIdentity>
{
    private static readonly ListFormat IdFormat = ListFormat.SlashSeparated;

    public static readonly UserIdentity None;
    public static readonly string DefaultSchema = "Default";
    public static readonly string InternalSchema = "internal";

    [DataMember(Order = 0), Key(0)]
    public string Id { get => field ?? ""; init; }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreMember]
    public string Schema => ParseId(Id).Schema;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreMember]
    public string Value => ParseId(Id).Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreMember]
    public bool IsValid => !Id.IsNullOrEmpty();

    // IStringLike — use Id (the round-trippable serialized form) rather than Value
    // (the computed schema-less component). Formatters go through this interface path.
    string IStringLike.Value => Id;
    public static UserIdentity Parse(string? s) => new(s ?? "");

    [method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public UserIdentity(string id)
        => Id = id;
    public UserIdentity(string provider, string providerBoundId)
        => Id = FormatId(provider, providerBoundId);

    // Conversion

    // NOTE: ToString() has to return Id.Value, otherwise dictionary key
    // serialization will be broken, and UserIdentity is actually used
    // as a dictionary key in User.Identities
    public override string ToString()
        => Id;

    public void Deconstruct(out string schema, out string value)
        => (schema, value) = ParseId(Id);

    public static implicit operator UserIdentity((string Schema, string Value) source)
        => new(source.Schema, source.Value);
    public static implicit operator UserIdentity(string source) => new(source);
    public static implicit operator string(UserIdentity source) => source.Id;

    // Equality

    public bool Equals(UserIdentity other)
        => string.Equals(Id, other.Id);
    public override int GetHashCode()
        => Id.GetOrdinalHashCode();

    // Comparison

    public int CompareTo(UserIdentity other)
        => string.CompareOrdinal(Id, other.Id);

    // Private methods

    private static string FormatId(string schema, string value)
    {
        using var f = IdFormat.CreateFormatter();
        if (schema != DefaultSchema)
            f.Append(schema);
        f.Append(value);
        f.AppendEnd();
        return f.Output;
    }

    private static (string Schema, string Value) ParseId(string id)
    {
        if (id.IsNullOrEmpty())
            return ("", "");

        using var p = IdFormat.CreateParser(id);
        if (!p.TryParseNext())
            return (DefaultSchema, id);

        var firstItem = p.Item;
        return p.TryParseNext()
            ? (firstItem, p.Item)
            : (DefaultSchema, firstItem);
    }
}
