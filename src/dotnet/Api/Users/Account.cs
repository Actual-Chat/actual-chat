using MemoryPack;
using ActualLab.Versioning;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record Account(
    [property: DataMember, MemoryPackOrder(0)] UserId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
) : IHasId<UserId>, IHasVersion<long>, IRequirementTarget
{
    public static readonly Requirement<Account> MustExist = Requirement.New(
        (Account? a) => a?.Id is not null,
        new(() => StandardError.NotFound<Account>()));
    public static readonly Requirement<Account> MustNotBeGuest = Requirement.New(
        (Account? a) => a?.Id is not null && !a.Id.IsGuest,
        new(() => StandardError.Account.Guest()));

    [DataMember, MemoryPackOrder(2)] public AccountStatus Status { get; init; }
    [DataMember, MemoryPackOrder(3)] public Avatar Avatar { get; init; } = null!; // Populated only on reads

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsGuest => Id.IsGuest;

    // This record relies on referential equality
    public virtual bool Equals(Account? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
