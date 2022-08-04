using Stl.Versioning;

namespace ActualChat.Users;

public record UserContact : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    public Symbol Id { get; init; } = Symbol.Empty;
    public long Version { get; init; }
    public Symbol OwnerUserId { get; init; } = Symbol.Empty;
    public Symbol TargetUserId { get; init; } = Symbol.Empty;
    public string Name { get; init; } = "";
    public bool IsFavorite { get; init; }
}

[DataContract]
public sealed record UserContactDiff : RecordDiff
{
    [DataMember] public Symbol OwnerUserId { get; init; }
    [DataMember] public Symbol TargetUserId { get; init; }
    [DataMember] public string? Name { get; init; } = "";
    [DataMember] public bool IsFavorite { get; init; }
}
