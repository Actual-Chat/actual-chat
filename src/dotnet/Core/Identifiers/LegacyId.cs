using System.ComponentModel;
using ActualLab.Fusion.Blazor;
using MemoryPack;
using MessagePack;

namespace ActualChat;

#pragma warning disable MA0097

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(Internal.SymbolIdentifierJsonConverter<NodeRef>))]
[Newtonsoft.Json.JsonConverter(typeof(Internal.SymbolIdentifierNewtonsoftJsonConverter<NodeRef>))]
[TypeConverter(typeof(Internal.SymbolIdentifierTypeConverter<LegacyId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
[method: SerializationConstructor, MemoryPackConstructor]
public readonly partial record struct LegacyId(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] Symbol Id
    ) : ISymbolIdentifier<LegacyId>
{
    public static LegacyId None => default;

    // Computed properties
    [IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;
    [IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;

    public static LegacyId Parse(string? s)
        => new(s);

    public static LegacyId ParseOrNone(string? s)
        => new(s);

    public static bool TryParse(string? s, out LegacyId result)
    {
        result = new (s);
        return true;
    }
}
