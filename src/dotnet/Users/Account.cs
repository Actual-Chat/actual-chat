using MemoryPack;
using Stl.Versioning;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record Account(
    [property: DataMember, MemoryPackOrder(0)] UserId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
) : IHasId<UserId>, IHasVersion<long>, IRequirementTarget
{
    public static Account None => AccountFull.None;
    public static Account Loading => AccountFull.Loading;

    public static readonly Requirement<Account> MustExist = Requirement.New(
        new(() => StandardError.NotFound<Account>()),
        (Account? a) => a is { IsNone: false });
    public static readonly Requirement<Account> MustNotBeGuest = Requirement.New(
        new(() => StandardError.Account.Guest()),
        (Account? a) => a?.IsGuestOrNone == false);

    [DataMember, MemoryPackOrder(2)] public AccountStatus Status { get; init; }
    [DataMember, MemoryPackOrder(3)] public Avatar Avatar { get; init; } = null!; // Populated only on reads

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsNone;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsGuest => Id.IsGuest;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsGuestOrNone => Id.IsGuestOrNone;

    // This record relies on referential equality
    public virtual bool Equals(Account? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
