using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record AccountFull(
    [property: DataMember, MemoryPackOrder(4)] User User,
    long Version = 0
    ) : Account(new UserId(User.Id, AssumeValid.Option), Version)
{
    public static new readonly AccountFull None = new(User.NewGuest(), 0) { Avatar = Avatar.None };
    public static new readonly AccountFull Loading = new(User.NewGuest(), -1) { Avatar = Avatar.Loading }; // Should differ by Id & Version from None

    public static new readonly Requirement<AccountFull> MustExist = Requirement.New(
        new(() => StandardError.NotFound<Account>()),
        (AccountFull? a) => a is { IsNone: false });
    public static new readonly Requirement<AccountFull> MustNotBeGuest = Requirement.New(
        new(() => StandardError.Account.Guest()),
        (AccountFull? a) => a?.IsGuestOrNone == false);
    public static readonly Requirement<AccountFull> MustBeAdmin = MustExist & Requirement.New(
        new(() => StandardError.Account.NonAdmin()),
        (AccountFull? a) => a?.IsAdmin ?? false);
    public static readonly Requirement<AccountFull> MustNotBeSuspended = MustExist & Requirement.New(
        new(() => StandardError.Account.Suspended()),
        (AccountFull? a) => a != null && (a.Status != AccountStatus.Suspended || a.IsAdmin));
    public static readonly Requirement<AccountFull> MustBeActive = MustNotBeGuest & Requirement.New(
        new(() => StandardError.Account.Inactive()),
        (AccountFull? a) => a != null && (a.Status == AccountStatus.Active || a.IsAdmin));

    [DataMember, MemoryPackOrder(5)] public bool IsAdmin { get; init; }
    [Obsolete("2023.07: Allows legacy clients to deserialize new version of this type.")]
    [DataMember, MemoryPackOrder(6)] public string LegacyPhone { get; private set; } = "";
    [DataMember, MemoryPackOrder(7)] public bool SyncContacts { get; init; }
    [DataMember, MemoryPackOrder(12)] public Phone Phone { get; init; }
    [DataMember, MemoryPackOrder(8)] public string Email { get; init; } = "";
    [DataMember, MemoryPackOrder(9)] public string Name { get; init; } = "";
    [DataMember, MemoryPackOrder(10)] public string LastName { get; init; } = "";
    [DataMember, MemoryPackOrder(11)] public string Username { get; init; } = "";
    [DataMember, MemoryPackOrder(13)] public bool IsGreetingCompleted { get; init; }
    [DataMember, MemoryPackOrder(14)] public bool IsEmailVerified { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string FullName => $"{Name} {LastName}".Trim();

    // This record relies on referential equality
    public bool Equals(AccountFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    // Deserialization handlers

#pragma warning disable CS0618
    private static readonly Action<AccountFull, Phone> _phoneSetter = typeof(AccountFull)
        .GetProperty(nameof(Phone))!
        .GetSetter<AccountFull, Phone>();

    [MemoryPackOnSerializing]
    private void OnSerializing()
        => LegacyPhone = Phone.Value;

    [MemoryPackOnDeserialized]
    private void OnDeserialized()
    {
        var legacyPhone = LegacyPhone;
        if (legacyPhone.IsNullOrEmpty())
            return;
        if (!Phone.IsNone)
            return;

        var phone = new Phone(legacyPhone, ParseOrNone.Option);
        _phoneSetter.Invoke(this, phone);
    }
#pragma warning restore CS0618
}
