using ActualLab.Versioning;

namespace ActualChat.WebHooks;

[DataContract, MessagePackObject]
public sealed partial record WebHook(
    [property: DataMember, Key(0)] WebHookId Id,
    [property: DataMember, Key(1)] long Version
) : IHasId<WebHookId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(2)] public required WebHookScope Scope { get; init; }
    [DataMember, Key(3)] public required string ScopeId { get; init; }
    [DataMember, Key(4)] public WebHookKind Kind { get; init; }
    [DataMember, Key(5)] public string Name { get; init; } = "";
    [DataMember, Key(6)] public UserId? CreatedBy { get; init; }
    [DataMember, Key(7)] public Moment CreatedAt { get; init; }
    [DataMember, Key(8)] public Moment ModifiedAt { get; init; }
    [DataMember, Key(9)] public bool IsEnabled { get; init; } = true;
    [DataMember, Key(10)] public WebHookDisabledReason DisabledReason { get; init; }
    [DataMember, Key(11)] public Moment? LastActivityAt { get; init; }
    [DataMember, Key(12)] public string Url { get; init; } = "";
    [DataMember, Key(13)] public WebHookEvents Events { get; init; }
    [DataMember, Key(14)] public bool IncludeText { get; init; } = true;
    [DataMember, Key(15)] public ApiArray<ChatId> ChatIds { get; init; }
    [DataMember, Key(16)] public bool SubscribeNotifications { get; init; }
    [DataMember, Key(17)] public string? CustomHeaderName { get; init; }
    [DataMember, Key(18)] public int ConsecutiveFailures { get; init; }
    [DataMember, Key(19)] public int? LastStatusCode { get; init; }
    [DataMember, Key(20)] public string? LastError { get; init; }
    // Keys 21..24 reserved for phase 2 (BotLocalId, DisplayName, AvatarMediaId, DefaultChatId)

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsActiveOutgoing => IsEnabled && Kind == WebHookKind.Outgoing;

    public bool Covers(ChatId chatId)
        => Scope switch {
            WebHookScope.Chat => ScopeId == chatId.Value,
            WebHookScope.Place => ChatIds.Count == 0 || ChatIds.Contains(chatId),
            _ => ChatIds.Contains(chatId),
        };
}
