namespace ActualChat.Users;

[StructLayout(LayoutKind.Auto)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptOut)]
public readonly partial record struct UserIdentity : IComparable<UserIdentity>
{
    private static readonly ListFormat IdFormat = ListFormat.SlashSeparated;

    public static readonly UserIdentity None;
    public static readonly string DefaultSchema = "Default";
    public static readonly string InternalSchema = "internal";

    [DataMember(Order = 0), MemoryPackOrder(0), StringAsSymbolMemoryPackFormatter]
    public string Id { get => field ?? ""; init; }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore, IgnoreMember]
    public string Schema => ParseId(Id).Schema;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore, IgnoreMember]
    public string Value => ParseId(Id).Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore, IgnoreMember]
    public bool IsValid => !Id.IsNullOrEmpty();

    [method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
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
