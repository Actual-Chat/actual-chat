namespace ActualChat.MLSearch.Documents;

internal sealed record ChatInfo(string Id, bool IsPublic, bool IsBotChat): IHasId<string>;
