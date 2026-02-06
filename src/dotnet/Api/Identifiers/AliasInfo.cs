namespace ActualChat;

/// <summary>
/// Contains an identifier and its optional alias.
/// </summary>
public abstract record AliasInfo(StringIdentifier UntypedId, AliasId? AliasId);

/// <summary>
/// Typed alias info containing an identifier and its optional alias.
/// </summary>
public sealed record AliasInfo<TId>(TId Id, AliasId? AliasId) : AliasInfo(Id, AliasId)
    where TId : StringIdentifier;
