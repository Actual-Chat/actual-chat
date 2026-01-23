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
    [DataMember, MemoryPackOrder(4)] public ApiMap<UserIdentity, string> Identities { get; init; }
    [DataMember, MemoryPackOrder(18)] public ApiMap<string, string> Claims { get; init; }

    // User - computed property for backwards compatibility
    [Obsolete("Use ToUser() method, or Identities, Claims, Name properties directly.")]
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public User User => ToUser();

    /// <summary>
    /// Creates a User instance from this AccountFull.
    /// Use this when you need a User object for operations that require it (e.g., updating DbUser).
    /// </summary>
    public User ToUser() => new(Id.Value, Name) {
        Version = Version,
        Claims = Claims,
        Identities = Identities,
    };

    // Account properties
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
    public AccountFull(User user, long version = 0) : base(UserId.ParseNullable(user.Id)!, version)
    {
        Identities = user.Identities;
        Claims = user.Claims;
        Name = user.Name;
    }

    [MemoryPackConstructor]
    public AccountFull(
        UserId id,
        long version,
        AccountStatus status,
        Avatar avatar,
        ApiMap<UserIdentity, string> identities,
        ApiMap<string, string> claims,
        bool isAdmin,
        bool syncContacts,
        Phone? phone,
        string email,
        string name,
        string username,
        bool isGreetingCompleted,
        bool isEmailVerified,
        Moment createdAt,
        string timeZone,
        AliasId? aliasId)
        : base(id, version)
    {
        Status = status;
        Avatar = avatar;
        Identities = identities;
        Claims = claims;
        IsAdmin = isAdmin;
        SyncContacts = syncContacts;
        Phone = phone;
        Email = email;
        Name = name;
        Username = username;
        IsGreetingCompleted = isGreetingCompleted;
        IsEmailVerified = isEmailVerified;
        CreatedAt = createdAt;
        TimeZone = timeZone;
        AliasId = aliasId;
    }

    // This record relies on referential equality
    public bool Equals(AccountFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
