using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Users;

// Note: Id can be null in certain scenarios:
//
// 1. Sign-in flow (production): When a new user signs in via OAuth, phone, or email,
//    a "template" AccountFull is created with null Id before being passed to the
//    AccountsBackend_SignIn command. The actual UserId is assigned inside the command
//    handler when the account is persisted to the database.
//    Locations: ServerAuth.cs, PhoneAuth.cs, EmailAuth.cs.
//
// 2. Tests: The AccountFull(string name) constructor creates accounts with null Id
//    for testing purposes. These get assigned real IDs when signed in via test helpers.
//
// All computed properties that access Id must handle the null case to avoid
// NullReferenceException during command tracing/logging (which calls ToString()).

/// <summary>
/// Represents a user account with associated avatar and status.
/// </summary>
[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public partial record Account(
    [property: DataMember, Key(0)] UserId Id,
    [property: DataMember, Key(1)] long Version = 0
) : IHasId<UserId>, IHasVersion<long>, IRequirementTarget
{
    public static readonly Requirement<Account> MustExist = Requirement.New(
        (Account? a) => a?.HasId() == true,
        new(() => StandardError.NotFound<Account>()));
    public static readonly Requirement<Account> MustNotBeGuest = Requirement.New(
        (Account? a) => a?.HasId() == true && !a.Id.IsGuest,
        new(() => StandardError.Account.Guest()));

    [DataMember, Key(2)] public AccountStatus Status { get; init; }
    [DataMember, Key(3)] public Avatar Avatar { get; init; } = null!; // Populated only on reads

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsGuest => Id?.IsGuest ?? true;

    // This record relies on referential equality
    public virtual bool Equals(Account? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
