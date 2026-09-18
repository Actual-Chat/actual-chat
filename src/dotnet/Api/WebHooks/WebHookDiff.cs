namespace ActualChat.WebHooks;

[DataContract, MessagePackObject(true)]
public sealed partial record WebHookDiff : RecordDiff
{
    [DataMember] public string? Name { get; init; }
    [DataMember] public string? Url { get; init; }
    [DataMember] public WebHookEvents? Events { get; init; }
    [DataMember] public bool? IncludeText { get; init; }
    [DataMember] public ApiArray<ChatId>? ChatIds { get; init; }
    [DataMember] public bool? SubscribeNotifications { get; init; }
    [DataMember] public string? CustomHeaderName { get; init; }
    [DataMember] public string? CustomHeaderValue { get; init; }
    [DataMember] public bool? IsEnabled { get; init; }
}
