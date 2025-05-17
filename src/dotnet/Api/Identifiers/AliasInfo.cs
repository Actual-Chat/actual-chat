namespace ActualChat;

public abstract record AliasInfo(StringIdentifier UntypedId, AliasId? AliasId);

public sealed record AliasInfo<TId>(TId Id, AliasId? AliasId) : AliasInfo(Id, AliasId)
    where TId : StringIdentifier;
