using Stl.Versioning;

namespace ActualChat.Users;

public record UserContact : IHasId<Symbol>, IHasVersion<long>
{
    public Symbol Id { get; init; } = Symbol.Empty;
    public long Version { get; set; }
    public Symbol OwnerUserId { get; init; } = Symbol.Empty;
    public Symbol TargetPrincipalId { get; init; } = Symbol.Empty;
    public string Name { get; init; } = "";
}
