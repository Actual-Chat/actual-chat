using ActualChat.Comparison;
using MemoryPack;
using Stl.Versioning;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record Account(
    [property: DataMember, MemoryPackOrder(0)] UserId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
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

    [DataMember, MemoryPackOrder(2)] public AccountStatus Status { get; init; }
    [DataMember, MemoryPackOrder(3)] public Avatar Avatar { get; init; } = null!; // Populated only on reads

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool IsNone => Id.IsNone;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool IsGuest => Id.IsGuest;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool IsGuestOrNone => Id.IsGuestOrNone;

    // This record relies on version-based equality
    public virtual bool Equals(Account? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}
