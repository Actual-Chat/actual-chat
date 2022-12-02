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
        (Account? a) => a is { Id.IsNone: false });
    public static Requirement<Account> MustNotBeGuest { get; } = Requirement.New(
        new(() => StandardError.Account.Guest()),
        (Account? a) => a?.IsGuest == false);

    [DataMember] public AccountStatus Status { get; init; }
    [DataMember] public Avatar Avatar { get; init; } = null!; // Populated only on reads

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsGuest => Id.IsGuestId;

    // This record relies on version-based equality
    public virtual bool Equals(Account? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}
