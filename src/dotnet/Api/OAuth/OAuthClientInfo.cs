namespace ActualChat.OAuth;

[DataContract, MessagePackObject]
public sealed partial record OAuthClientInfo(
    [property: DataMember, Key(0)] string ClientId,
    [property: DataMember, Key(1)] string ClientName,
    [property: DataMember, Key(2)] ApiArray<string> RedirectHosts,
    [property: DataMember, Key(3)] bool IsLoopback,
    [property: DataMember, Key(4)] ApiArray<string> Scopes);
