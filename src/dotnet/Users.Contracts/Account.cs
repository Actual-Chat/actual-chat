namespace ActualChat.Users;

[DataContract]
public sealed record Account(
    [property: DataMember] Symbol Id,
    [property: DataMember] User User) : IHasId<Symbol>, IRequirementTarget
{
    public static Account Guest { get; } = new(Symbol.Empty, User.NewGuest()) { Status = AccountStatus.Inactive };

    public static Requirement<Account> MustExist { get; } = Requirement.New(
        new(() => StandardError.Account.None()),
        (Account? a) => a != null);
    public static Requirement<Account> MustBeAdmin { get; } = MustExist & Requirement.New(
        new(() => StandardError.Account.NonAdmin()),
        (Account? a) => a?.IsAdmin ?? false);
    public static Requirement<Account> MustNotBeInactive { get; } = MustExist & Requirement.New(
        new(() => StandardError.Account.Inactive()),
        (Account? a) => a != null && (a.Status != AccountStatus.Inactive || a.IsAdmin));
    public static Requirement<Account> MustNotBeSuspended { get; } = MustExist & Requirement.New(
        new(() => StandardError.Account.Suspended()),
        (Account? a) => a != null && (a.Status != AccountStatus.Suspended || a.IsAdmin));
    public static Requirement<Account> MustBeActive { get; } = MustNotBeSuspended & MustNotBeInactive;

    [DataMember] public long Version { get; init; }
    [DataMember] public AccountStatus Status { get; init; }
    [DataMember] public bool IsAdmin { get; init; }
    [DataMember] public Avatar Avatar { get; init; }
}
