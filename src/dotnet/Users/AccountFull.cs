namespace ActualChat.Users;

[DataContract]
public sealed record AccountFull(
    UserId Id,
    [property: DataMember] User User
    ) : Account(Id)
{
    public static new AccountFull None { get; } = new(default, User.NewGuest()) { Avatar = Avatar.None };
    public static new AccountFull Loading { get; } = new(default, User.NewGuest()) { Avatar = Avatar.Loading }; // Should differ by ref. from None

    public static new Requirement<AccountFull> MustExist { get; } = Requirement.New(
        new(() => StandardError.Account.None()),
        (AccountFull? a) => a != null);
    public static Requirement<AccountFull> MustBeAdmin { get; } = MustExist & Requirement.New(
        new(() => StandardError.Account.NonAdmin()),
        (AccountFull? a) => a?.IsAdmin ?? false);
    public static Requirement<AccountFull> MustNotBeInactive { get; } = MustExist & Requirement.New(
        new(() => StandardError.Account.Inactive()),
        (AccountFull? a) => a != null && (a.Status != AccountStatus.Inactive || a.IsAdmin));
    public static Requirement<AccountFull> MustNotBeSuspended { get; } = MustExist & Requirement.New(
        new(() => StandardError.Account.Suspended()),
        (AccountFull? a) => a != null && (a.Status != AccountStatus.Suspended || a.IsAdmin));
    public static Requirement<AccountFull> MustBeActive { get; } = MustNotBeSuspended & MustNotBeInactive;

    [DataMember] public bool IsAdmin { get; init; }
}
