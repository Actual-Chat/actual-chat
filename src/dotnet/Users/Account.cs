using ActualChat.Comparison;
using Stl.Versioning;

namespace ActualChat.Users;

[DataContract]
public record Account(
    [property: DataMember] UserId Id,
    [property: DataMember] long Version = 0
) : IHasId<UserId>, IHasVersion<long>, IRequirementTarget
{
    public static IdAndVersionEqualityComparer<Account, UserId> EqualityComparer { get; } = new();

    public static Account None => AccountFull.None;
    public static Account Loading => AccountFull.Loading;

    public static Requirement<Account> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Account>()),
        (Account? a) => a is { IsNone: false });
    public static Requirement<Account> MustNotBeGuest { get; } = Requirement.New(
        new(() => StandardError.Account.Guest()),
        (Account? a) => a?.IsGuestOrNone == false);

    [DataMember] public AccountStatus Status { get; init; }
    [DataMember] public Avatar Avatar { get; init; } = null!; // Populated only on reads

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsNone => Id.IsNone;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsGuest => Id.IsGuest;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsGuestOrNone => Id.IsGuestOrNone;

    public Account() : this(UserId.None) { }

    // This record relies on version-based equality
    public virtual bool Equals(Account? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}
