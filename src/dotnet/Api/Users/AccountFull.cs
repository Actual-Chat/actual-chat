using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record AccountFull : Account
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

    // User properties (flattened from User type)
    // Identities is hidden from JSON/DataContract, JsonCompatibleIdentities is used instead
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ApiMap<UserIdentity, string> Identities { get; init; }

    // This property is used for JSON serialization - converts UserIdentity keys to strings
    [DataMember(Name = nameof(Identities)), MemoryPackOrder(4)]
    [JsonPropertyName(nameof(Identities)), Newtonsoft.Json.JsonProperty(nameof(Identities))]
    public ApiMap<string, string> JsonCompatibleIdentities {
        get => Identities.UnorderedItems.ToApiMap(p => p.Key.Id, p => p.Value, StringComparer.Ordinal);
        init => Identities = value.ToApiMap(p => new UserIdentity(p.Key), p => p.Value);
    }

    [DataMember, MemoryPackOrder(18)] public ApiMap<string, string> Claims { get; init; }

    // Account properties
    [DataMember, MemoryPackOrder(5)] public bool IsAdmin { get; init; }
    [DataMember, MemoryPackOrder(7)] public bool SyncContacts { get; init; }
    [DataMember, MemoryPackOrder(12)] public Phone? Phone { get; init; }
    [DataMember, MemoryPackOrder(8)] public string Email { get; init; } = "";
    [DataMember, MemoryPackOrder(9)] public string Name { get; init; } = "";
    [Obsolete("2025.01: Unused, will be removed soon.")]
    [DataMember, MemoryPackOrder(11)] public string Username { get; init; } = "";
    [DataMember, MemoryPackOrder(13)] public bool IsGreetingCompleted { get; init; }
    [DataMember, MemoryPackOrder(14)] public bool IsEmailVerified { get; init; }
    [DataMember, MemoryPackOrder(15)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(16)] public string TimeZone { get; init; } = "";
    [DataMember, MemoryPackOrder(17)] public AliasId? AliasId { get; init; }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public AliasInfo<UserId> AliasInfo => field ??= new(Id!, AliasId);

    [Obsolete("This property is for backward compatibility. Use Identities, Claims, Name properties directly.")]
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
#pragma warning disable CS0618 // Type or member is obsolete
    public LegacyUser User => new(Id?.Value ?? "", Name) {
        Version = Version,
        Claims = Claims,
        Identities = Identities,
    };
#pragma warning restore CS0618

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public AccountFull(UserId id, long version = 0) : base(id, version)
    {
        Identities = ApiMap<UserIdentity, string>.Empty;
        Claims = ApiMap<string, string>.Empty;
    }

    // Constructor for tests - creates AccountFull with just a name
    public AccountFull(string name) : base(null!, 0)
    {
        Name = name;
        Identities = ApiMap<UserIdentity, string>.Empty;
        Claims = ApiMap<string, string>.Empty;
    }

    [Obsolete("Use constructor with UserId parameter.")]
#pragma warning disable CS0618 // Type or member is obsolete
    public AccountFull(LegacyUser user, long version = 0) : base(UserId.ParseNullable(user.Id)!, version)
    {
        Identities = user.Identities;
        Claims = user.Claims;
        Name = user.Name;
    }
#pragma warning restore CS0618

    // This record relies on referential equality
    public bool Equals(AccountFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
