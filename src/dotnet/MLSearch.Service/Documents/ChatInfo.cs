namespace ActualChat.MLSearch.Documents;

internal sealed record ChatInfo(ChatId ChatId, bool IsPublic, bool IsBotChat) : IHasId<string>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Id => ChatId;
}
