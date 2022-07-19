namespace ActualChat.Users;

public sealed record Account(Symbol Id, User User) : IHasId<Symbol>, IRequirementTarget
{
    public static Account Guest { get; } = new(Symbol.Empty, User.NewGuest()) { Status = AccountStatus.Inactive };

    public static Requirement<Account> MustExist { get; } = Requirement.New(
        new(() => new NoAccountException()),
        (Account? p) => p != null);
    public static Requirement<Account> MustBeAdmin { get; } = MustExist & Requirement.New(
        new(() => new NonAdminAccountException()),
        (Account? p) => p?.IsAdmin ?? false);
    public static Requirement<Account> MustNotBeInactive { get; } = MustExist & Requirement.New(
        new(() => new InactiveAccountException()),
        (Account? p) => p != null && (p.Status != AccountStatus.Inactive || p.IsAdmin));
    public static Requirement<Account> MustNotBeSuspended { get; } = MustExist & Requirement.New(
        new(() => new SuspendedAccountException()),
        (Account? p) => p != null && (p.Status != AccountStatus.Suspended || p.IsAdmin));
    public static Requirement<Account> MustBeActive { get; } = MustNotBeSuspended & MustNotBeInactive;

    public long Version { get; init; }
    public AccountStatus Status { get; init; }
    public Symbol AvatarId { get; init; }
    public bool IsAdmin { get; init; }
}
