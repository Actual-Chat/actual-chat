using ActualLab.Fusion.Blazor;

namespace ActualChat.Users;

/// <summary>
/// Extended <see cref="Account"/> with full user details including identities and claims.
/// </summary>
[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record AccountFull : Account
{
    public static new readonly Requirement<AccountFull> MustExist = Requirement.New(
        (AccountFull? a) => a?.HasId() == true,
        new(() => StandardError.NotFound<Account>()));
    public static new readonly Requirement<AccountFull> MustNotBeGuest = Requirement.New(
        (AccountFull? a) => a?.HasId() == true && !a.Id.IsGuest,
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

    private static Action<AccountFull, Phone> PhoneSetter
        => field ??= typeof(AccountFull).GetProperty(nameof(Phone))!.GetSetter<AccountFull, Phone>();

    [DataMember, Key(20)] public int FormatVersion { get; init; }
    [DataMember, Key(10)] public bool IsAdmin { get; init; }
    [DataMember, Key(11)] public bool SyncContacts { get; init; }
    [DataMember, Key(14)] public Phone? Phone { get; init; }
    [DataMember, Key(12)] public string Email { get; init; } = "";
    [DataMember, Key(13)] public string Name { get; init; } = "";
    [DataMember, Key(15)] public bool IsGreetingCompleted { get; init; }
    [DataMember, Key(16)] public Moment CreatedAt { get; init; }
    [DataMember, Key(17)] public string TimeZone { get; init; } = "";
    [DataMember, Key(18)] public AliasId? AliasId { get; init; }

    // User properties (flattened from User type)

    // Hidden from JSON/DataContract, JsonCompatibleIdentities is used instead
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ApiMap<UserIdentity, string> Identities { get; init; }

    [DataMember, Key(19)] public ApiMap<string, string> Claims { get; init; }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public AliasInfo<UserId> AliasInfo => field ??= new(Id, AliasId);

    // Implements JSON serialization for Identities (converts UserIdentity keys to strings)
    [DataMember(Name = nameof(Identities)), Key(21)]
    [JsonPropertyName(nameof(Identities)), Newtonsoft.Json.JsonProperty(nameof(Identities))]
    public ApiMap<string, string> JsonCompatibleIdentities {
        get => Identities.UnorderedItems.ToApiMap(p => p.Key.Id, p => p.Value);
        init => Identities = value.ToApiMap(p => new UserIdentity(p.Key), p => p.Value);
    }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public AccountFull(UserId id, long version = 0) : base(id, version)
    {
        Identities = new ApiMap<UserIdentity, string>();
        Claims = new ApiMap<string, string>();
    }

    // Constructor for tests - creates AccountFull with just a name
    public AccountFull(string name) : base(null!, 0)
    {
        Name = name;
        Identities = new ApiMap<UserIdentity, string>();
        Claims = new ApiMap<string, string>();
    }

    // This record relies on referential equality
    public bool Equals(AccountFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
