using MemoryPack;

namespace ActualChat.AiSearch;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public readonly partial struct AiSearchChatId : ISymbolIdentifier<AiSearchChatId>
{
    public static AiSearchChatId None => throw new NotImplementedException();

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id => throw new NotImplementedException();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => throw new NotImplementedException();
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => throw new NotImplementedException();

    public static AiSearchChatId Parse(string? s) => throw new NotImplementedException();
    public static AiSearchChatId ParseOrNone(string s) => throw new NotImplementedException();
    public static bool TryParse(string? s, out AiSearchChatId result) => throw new NotImplementedException();
    public bool Equals(AiSearchChatId other) => throw new NotImplementedException();
}
