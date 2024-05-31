using MemoryPack;

namespace ActualChat.MLSearch;

using MLSearchChatId = ChatId;

/*
// TODO: this is a stub, remove or implement properly
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public readonly partial struct MLSearchChatId : ISymbolIdentifier<MLSearchChatId>
{
    public static MLSearchChatId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    public static MLSearchChatId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<MLSearchChatId>(s);
    public static MLSearchChatId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : None;
    public static bool TryParse(string? s, out MLSearchChatId result)
    {
        result = default;
        return false;
    }

    public bool Equals(MLSearchChatId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is MLSearchChatId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(MLSearchChatId left, MLSearchChatId right) => left.Equals(right);
    public static bool operator !=(MLSearchChatId left, MLSearchChatId right) => !left.Equals(right);
}
*/