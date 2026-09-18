namespace ActualChat.WebHooks;

public static class WebHookScopeIds
{
    public static ShardKey ToShardKey(WebHookScope scope, string scopeId)
        => scope switch {
            WebHookScope.Chat => ChatId.Parse(scopeId).ShardKey,
            WebHookScope.Place => PlaceId.Parse(scopeId).ShardKey,
            _ => UserId.Parse(scopeId).ShardKey,
        };
}
