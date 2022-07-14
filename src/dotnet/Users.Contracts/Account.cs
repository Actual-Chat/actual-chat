using System.Security;

namespace ActualChat.Users;

public sealed record Account(Symbol Id, User User) : IHasId<Symbol>, IRequirementTarget
{
    public static Account Guest { get; } = new(Symbol.Empty, User.NewGuest()) { Status = AccountStatus.Inactive };

    public static Requirement<Account> MustBeActive { get; } = Requirement.New(
        new("User is either suspended or not activated yet.", m => new SecurityException(m)),
        (Account? p) => p != null && (p.IsActive() || p.IsAdmin));
    public static Requirement<Account> MustBeAdmin { get; } = Requirement.New(
        new("Only administrators can perform this action.", m => new SecurityException(m)),
        (Account? p) => p?.IsAdmin ?? false);

    public long Version { get; init; }
    public AccountStatus Status { get; init; }
    public Symbol AvatarId { get; init; }
    public bool IsAdmin { get; init; }
}
