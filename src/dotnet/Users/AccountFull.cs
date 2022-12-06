namespace ActualChat.Users;

[DataContract]
public sealed record AccountFull(
    [property: DataMember] User User,
    long Version = 0
    ) : Account(User != null! ? new UserId(User.Id, AssumeValid.Option) : UserId.None, Version)
{
    public static new AccountFull None { get; } = new(User.NewGuest(), 0) { Avatar = Avatar.None };
    public static new AccountFull Loading { get; } = new(User.NewGuest(), 1) { Avatar = Avatar.Loading }; // Should differ by Id & Version from None

    public static new Requirement<AccountFull> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Account>()),
        (AccountFull? a) => a is { Id.IsNone: false });
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

    public AccountFull() : this(null!) { }

    // This record relies on version-based equality
    public bool Equals(AccountFull? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}
