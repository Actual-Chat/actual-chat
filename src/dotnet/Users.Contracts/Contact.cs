using Stl.Versioning;

namespace ActualChat.Users;

[DataContract]
public record Contact : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public Symbol Id { get; init; } = Symbol.Empty;
    [DataMember] public long Version { get; init; }
    [DataMember] public Symbol OwnerUserId { get; init; } = Symbol.Empty;
    [DataMember] public Symbol TargetUserId { get; init; } = Symbol.Empty;
    [DataMember] public Avatar Avatar { get; init; } = null!;
}

[DataContract]
public sealed record ContactDiff : RecordDiff
{
    [DataMember] public Symbol OwnerUserId { get; init; }
    [DataMember] public Symbol TargetUserId { get; init; }
}
