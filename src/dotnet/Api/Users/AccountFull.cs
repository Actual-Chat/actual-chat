using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record AccountFull(
    [property: DataMember, MemoryPackOrder(4)] User User,
    long Version = 0
    ) : Account(UserId.Parse(User.Id), Version)
{
    public static new readonly Requirement<AccountFull> MustExist = Requirement.New(
        (AccountFull? a) => a?.Id is not null,
        new(() => StandardError.NotFound<Account>()));
    public static new readonly Requirement<AccountFull> MustNotBeGuest = Requirement.New(
        (AccountFull? a) => a?.Id is not null && !a.Id.IsGuest,
        new(() => StandardError.Account.Guest()));
    public static readonly Requirement<AccountFull> MustBeAdmin = MustExist & Requirement.New(
        (AccountFull? a) => a?.IsAdmin ?? false,
        new(() => StandardError.Account.NonAdmin()));
    public static readonly Requirement<AccountFull> MustNotBeSuspended = MustExist & Requirement.New(
        (AccountFull? a) => a is not null && (a.Status != AccountStatus.Suspended || a.IsAdmin),
        new(() => StandardError.Account.Suspended()));
    public static readonly Requirement<AccountFull> MustBeActive = MustNotBeGuest & Requirement.New(
        (AccountFull? a) => a is not null && (a.Status == AccountStatus.Active || a.IsAdmin),
        new(() => StandardError.Account.Inactive()));

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require ...")]
    private static Action<AccountFull, Phone> PhoneSetter
        => field ??= typeof(AccountFull).GetProperty(nameof(Phone))!.GetSetter<AccountFull, Phone>();

    [DataMember, MemoryPackOrder(5)] public bool IsAdmin { get; init; }
    [DataMember, MemoryPackOrder(7)] public bool SyncContacts { get; init; }
    [DataMember, MemoryPackOrder(12)] public Phone? Phone { get; init; }
    [DataMember, MemoryPackOrder(8)] public string Email { get; init; } = "";
    [DataMember, MemoryPackOrder(9)] public string Name { get; init; } = "";
    [DataMember, MemoryPackOrder(11)] public string Username { get; init; } = "";
    [DataMember, MemoryPackOrder(13)] public bool IsGreetingCompleted { get; init; }
    [DataMember, MemoryPackOrder(14)] public bool IsEmailVerified { get; init; }
    [DataMember, MemoryPackOrder(15)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(16)] public string TimeZone { get; init; } = "";
    [DataMember, MemoryPackOrder(17)] public AliasId? AliasId { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public AliasInfo<UserId> AliasInfo => field ??= new(Id, AliasId);

    // This record relies on referential equality
    public bool Equals(AccountFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
