using MemoryPack;

namespace ActualChat.MLSearch;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public readonly partial struct MLSearchChatId : ISymbolIdentifier<MLSearchChatId>
{
    public static MLSearchChatId None => throw new NotImplementedException();

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id => throw new NotImplementedException();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => throw new NotImplementedException();
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => throw new NotImplementedException();

    public static MLSearchChatId Parse(string? s) => throw new NotImplementedException();
    public static MLSearchChatId ParseOrNone(string s) => throw new NotImplementedException();
    public static bool TryParse(string? s, out MLSearchChatId result) => throw new NotImplementedException();
    public bool Equals(MLSearchChatId other) => throw new NotImplementedException();
}
