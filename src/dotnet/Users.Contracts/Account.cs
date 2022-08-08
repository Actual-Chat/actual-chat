namespace ActualChat.Users;

public sealed record Account(Symbol Id, User User) : IHasId<Symbol>, IRequirementTarget
{
    public static Account Guest { get; } = new(Symbol.Empty, User.NewGuest()) { Status = AccountStatus.Inactive };

    public static Requirement<Account> MustExist { get; } = Requirement.New(
        new(() => StandardError.Account.None()),
        (Account? p) => p != null);
    public static Requirement<Account> MustBeAdmin { get; } = MustExist & Requirement.New(
        new(() => StandardError.Account.NonAdmin()),
        (Account? p) => p?.IsAdmin ?? false);
    public static Requirement<Account> MustNotBeInactive { get; } = MustExist & Requirement.New(
        new(() => StandardError.Account.Inactive()),
        (Account? p) => p != null && (p.Status != AccountStatus.Inactive || p.IsAdmin));
    public static Requirement<Account> MustNotBeSuspended { get; } = MustExist & Requirement.New(
        new(() => StandardError.Account.Suspended()),
        (Account? p) => p != null && (p.Status != AccountStatus.Suspended || p.IsAdmin));
    public static Requirement<Account> MustBeActive { get; } = MustNotBeSuspended & MustNotBeInactive;

    // Must be used for other people's accounts only!
    public static Requirement<Account> MustBeAvailable { get; } = Requirement.New(
        new(() => StandardError.Account.Unavailable()),
        (Account? p) => p != null);

    public long Version { get; init; }
    public AccountStatus Status { get; init; }
    public Symbol AvatarId { get; init; }
    public bool IsAdmin { get; init; }
}
