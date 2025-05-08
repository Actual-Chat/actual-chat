using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
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
    [field: AllowNull, MaybeNull]
    private static Action<AccountFull, Phone> PhoneSetter
        => field ??= typeof(AccountFull).GetProperty(nameof(Phone))!.GetSetter<AccountFull, Phone>();

    [DataMember, MemoryPackOrder(5)] public bool IsAdmin { get; init; }
    [Obsolete("2023.07: Allows legacy clients to deserialize new version of this type.")]
    [DataMember, MemoryPackOrder(6)] public string LegacyPhone { get; private set; } = "";
    [DataMember, MemoryPackOrder(7)] public bool SyncContacts { get; init; }
    [DataMember, MemoryPackOrder(12)] public Phone? Phone { get; init; }
    [DataMember, MemoryPackOrder(8)] public string Email { get; init; } = "";
    [DataMember, MemoryPackOrder(9)] public string Name { get; init; } = "";
    [Obsolete("2024.11: Allows legacy clients to get/set LastName.")]
    [DataMember, MemoryPackOrder(10)] public string LastName { get; init; } = "";
    [DataMember, MemoryPackOrder(11)] public string Username { get; init; } = "";
    [DataMember, MemoryPackOrder(13)] public bool IsGreetingCompleted { get; init; }
    [DataMember, MemoryPackOrder(14)] public bool IsEmailVerified { get; init; }
    [DataMember, MemoryPackOrder(15)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(16)] public string TimeZone { get; init; } = "";
    [DataMember, MemoryPackOrder(17)] public AliasId? AliasId { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    [Obsolete("2024.11: Allows legacy clients to get FullName.")]
    public string FullName => (LastName.IsNullOrEmpty() ? Name : $"{Name} {LastName}").Trim();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    [field: AllowNull, MaybeNull]
    public AliasInfo<UserId> AliasInfo => field ??= new(Id, AliasId);

    // This record relies on referential equality
    public bool Equals(AccountFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    // Private methods

#pragma warning disable CS0618
    [MemoryPackOnSerializing]
    private void OnSerializing()
        => LegacyPhone = Phone?.Value ?? "";

    [MemoryPackOnDeserialized]
    private void OnDeserialized()
    {
        var legacyPhone = LegacyPhone;
        if (legacyPhone.IsNullOrEmpty())
            return;
        if (Phone != null)
            return;

        var phone = Phone.Parse(legacyPhone);
        PhoneSetter.Invoke(this, phone);
    }
#pragma warning restore CS0618
}
