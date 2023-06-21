using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record AccountFull(
    [property: DataMember, MemoryPackOrder(4)] User User,
    long Version = 0
    ) : Account(new UserId(User.Id, AssumeValid.Option), Version)
{
    public static new AccountFull None { get; } = new(User.NewGuest(), 0) { Avatar = Avatar.None };
    public static new AccountFull Loading { get; } = new(User.NewGuest(), -1) { Avatar = Avatar.Loading }; // Should differ by Id & Version from None

    public static new Requirement<AccountFull> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Account>()),
        (AccountFull? a) => a is { IsNone: false });
    public static new Requirement<AccountFull> MustNotBeGuest { get; } = Requirement.New(
        new(() => StandardError.Account.Guest()),
        (AccountFull? a) => a?.IsGuestOrNone == false);
    public static Requirement<AccountFull> MustBeAdmin { get; } = MustExist & Requirement.New(
        new(() => StandardError.Account.NonAdmin()),
        (AccountFull? a) => a?.IsAdmin ?? false);
    public static Requirement<AccountFull> MustNotBeSuspended { get; } = MustExist & Requirement.New(
        new(() => StandardError.Account.Suspended()),
        (AccountFull? a) => a != null && (a.Status != AccountStatus.Suspended || a.IsAdmin));
    public static Requirement<AccountFull> MustBeActive { get; } = MustNotBeGuest & Requirement.New(
        new(() => StandardError.Account.Inactive()),
        (AccountFull? a) => a != null && (a.Status == AccountStatus.Active || a.IsAdmin));

    [DataMember, MemoryPackOrder(5)] public bool IsAdmin { get; init; }
    [DataMember, MemoryPackOrder(6)] public string Phone { get; init; } = "";
    [DataMember, MemoryPackOrder(7)] public bool SyncContacts { get; init; }
    [DataMember, MemoryPackOrder(8)] public string Email { get; init; } = "";
    [DataMember, MemoryPackOrder(9)] public string Name { get; init; } = "";
    [DataMember, MemoryPackOrder(10)] public string LastName { get; init; } = "";
    [DataMember, MemoryPackOrder(11)] public string Username { get; init; } = "";

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public string FullName => $"{Name} {LastName}".Trim();

    // This record relies on version-based equality
    public bool Equals(AccountFull? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}
