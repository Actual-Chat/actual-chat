using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Users.Db;

[Index(nameof(IsGreetingCompleted))]
[Index(nameof(Version), nameof(Id))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbAccount : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private NewtonsoftJsonSerialized<ImmutableDictionary<string, string>> _claims
        = ImmutableDictionary<string, string>.Empty;

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    // FormatVersion: 0 = legacy (need to read Claims from DbUser), 1 = all data in DbAccount
    public int FormatVersion { get; set; }

    [Column(TypeName = "smallint")]
    public AccountStatus Status { get; set; }
    public string Email { get; set; } = "";
    public bool IsEmailVerified { get; set; }
    public string Phone { get; set; } = "";
    public bool SyncContacts { get; set; }
    public string Name { get; set; } = "";
    public bool IsGreetingCompleted { get; set; }
    public string TimeZone { get; set; } = "";
    public string AliasId { get; set; } = "";
    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "We assume server-side code is fully preserved")]
    public string ClaimsJson {
        get => _claims.Data;
        set => _claims = value;
    }

    [NotMapped]
    public ImmutableDictionary<string, string> Claims {
        get => _claims.Value;
        set => _claims = value;
    }

    public List<DbAccountIdentity> Identities { get; } = new();

    // ToModel: for FormatVersion >= 1 accounts that have all data in DbAccount
    public AccountFull ToModel()
    {
        var identities = Identities.ToApiMap(ai => new UserIdentity(ai.Id), ai => ai.Secret);
        // For pre-v2 accounts: if IsEmailVerified flag is set but email identity doesn't exist, add it
        if (FormatVersion < 2 && IsEmailVerified && !Email.IsNullOrEmpty()) {
            if (ActualChat.Email.TryParse(Email, out var email) && !identities.HasEmail(Email))
                identities = identities.WithEmailIdentity(email);
        }
        return new(UserId.Parse(Id), Version) {
            FormatVersion = FormatVersion,
            Status = Status,
            Email = Email,
            Phone = !Phone.IsNullOrEmpty() ? ActualChat.Phone.Parse(Phone) : null,
            SyncContacts = SyncContacts,
            Name = Name,
            IsGreetingCompleted = IsGreetingCompleted,
            CreatedAt = CreatedAt,
            TimeZone = TimeZone,
            AliasId = AliasId.IsNullOrEmpty() ? null : ActualChat.AliasId.Parse(AliasId),
            Identities = identities,
            Claims = Claims.ToApiMap(),
        };
    }

    // ToModel: uses DbUser for Identities/Name/Claims when FormatVersion == 0
    public AccountFull ToModel(DbUser dbUser)
    {
        var identities = FormatVersion >= 1
            ? Identities.ToApiMap(ai => new UserIdentity(ai.Id), ai => ai.Secret)
            : dbUser.Identities.ToApiMap(ui => new UserIdentity(ui.Id), ui => ui.Secret);
        // For pre-v2 accounts: if IsEmailVerified flag is set but email identity doesn't exist, add it
        if (FormatVersion < 2 && IsEmailVerified && !Email.IsNullOrEmpty()) {
            if (ActualChat.Email.TryParse(Email, out var email) && !identities.HasEmail(Email))
                identities = identities.WithEmailIdentity(email);
        }
        return new(UserId.Parse(Id), Version) {
            FormatVersion = FormatVersion,
            Status = Status,
            Email = Email,
            Phone = !Phone.IsNullOrEmpty() ? ActualChat.Phone.Parse(Phone) : null,
            SyncContacts = SyncContacts,
            Name = FormatVersion >= 1 ? Name : dbUser.Name,
            IsGreetingCompleted = IsGreetingCompleted,
            CreatedAt = CreatedAt,
            TimeZone = TimeZone,
            AliasId = AliasId.IsNullOrEmpty() ? null : ActualChat.AliasId.Parse(AliasId),
            Identities = identities,
            Claims = FormatVersion >= 1 ? Claims.ToApiMap() : dbUser.Claims.ToApiMap(),
        };
    }

    public AccountFull ToModel(
        ApiMap<UserIdentity, string> identities,
        ApiMap<string, string> claims)
    {
        // For pre-v2 accounts: if IsEmailVerified flag is set but email identity doesn't exist, add it
        if (FormatVersion < 2 && IsEmailVerified && !Email.IsNullOrEmpty()) {
            if (ActualChat.Email.TryParse(Email, out var email) && !identities.HasEmail(Email))
                identities = identities.WithEmailIdentity(email);
        }
        return new(UserId.Parse(Id), Version) {
            FormatVersion = FormatVersion,
            Status = Status,
            Email = Email,
            Phone = !Phone.IsNullOrEmpty() ? ActualChat.Phone.Parse(Phone) : null,
            SyncContacts = SyncContacts,
            Name = Name,
            IsGreetingCompleted = IsGreetingCompleted,
            CreatedAt = CreatedAt,
            TimeZone = TimeZone,
            AliasId = AliasId.IsNullOrEmpty() ? null : ActualChat.AliasId.Parse(AliasId),
            Identities = identities,
            Claims = claims,
        };
    }

    public void UpdateFrom(AccountFull model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireSomeVersion();

        Id = id.Value;
        Version = model.Version;
        FormatVersion = 2; // Always upgrade to format version 2 on write
        Status = model.Status;
        Phone = model.Phone?.Value ?? "";
        SyncContacts = model.SyncContacts;
        Email = model.Email;
        IsEmailVerified = model.IsEmailVerified();
        Name = model.Name;
        IsGreetingCompleted = model.IsGreetingCompleted;
        CreatedAt = model.CreatedAt;
        TimeZone = model.TimeZone;
        AliasId = model.AliasId?.NormalizedValue ?? "";
        Claims = model.Claims.ToImmutableDictionary(StringComparer.Ordinal);

        // Add + update identities
        var identities = Identities.ToDictionary(ai => ai.Id, StringComparer.Ordinal);
        foreach (var (userIdentity, secret) in model.Identities) {
            if (!userIdentity.IsValid)
                continue;
            var foundIdentity = identities.GetValueOrDefault(userIdentity.Id);
            if (foundIdentity is not null) {
                foundIdentity.Secret = secret;
                continue;
            }
            Identities.Add(new DbAccountIdentity {
                Id = userIdentity.Id,
                DbAccountId = Id,
                Secret = secret ?? "",
            });
        }
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbAccount>
    {
        public void Configure(EntityTypeBuilder<DbAccount> builder) {
            builder.Property(a => a.Id).IsRequired();
            builder.HasIndex(a => new { a.Id, a.TimeZone });
            builder.HasIndex(a => new { a.AliasId })
                .HasFilter("alias_id <> ''")
                .IsUnique();
        }
    }
}
