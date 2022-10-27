using Stl.Versioning;

namespace ActualChat.Users;

[DataContract]
public record UserContact : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public Symbol Id { get; init; } = Symbol.Empty;
    [DataMember] public long Version { get; init; }
    [DataMember] public Symbol OwnerUserId { get; init; } = Symbol.Empty;
    [DataMember] public Symbol TargetUserId { get; init; } = Symbol.Empty;
    [DataMember] public string Title { get; init; } = "";
    [DataMember] public Avatar? Avatar { get; init; }
}

[DataContract]
public sealed record UserContactDiff : RecordDiff
{
    [DataMember] public Symbol OwnerUserId { get; init; }
    [DataMember] public Symbol TargetUserId { get; init; }
}
