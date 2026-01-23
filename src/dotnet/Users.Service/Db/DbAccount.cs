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
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    [Column(TypeName = "smallint")]
    public AccountStatus Status { get; set; }
    public string Email { get; set; } = "";
    public bool IsEmailVerified { get; set; }
    public string Phone { get; set; } = "";
    public bool SyncContacts { get; set; }
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";
    public string? UsernameNormalized { get; set; }
    public bool IsGreetingCompleted { get; set; }
    public string TimeZone { get; set; } = "";
    public string AliasId { get; set; } = "";
    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

#pragma warning disable CS0618 // Type or member is obsolete
    public AccountFull ToModel(LegacyUser user)
        => new(UserId.Parse(Id), Version) {
            Status = Status,
            Email = Email,
            IsEmailVerified = IsEmailVerified,
            Phone = !Phone.IsNullOrEmpty() ? ActualChat.Phone.Parse(Phone) : null,
            SyncContacts = SyncContacts,
            Name = user.Name, // Use user's name from users table for consistency
            Username = Username,
            IsGreetingCompleted = IsGreetingCompleted,
            CreatedAt = CreatedAt,
            TimeZone = TimeZone,
            AliasId = AliasId.IsNullOrEmpty() ? null : ActualChat.AliasId.Parse(AliasId),
            Identities = user.Identities,
            Claims = user.Claims,
        };
#pragma warning restore CS0618

    public AccountFull ToModel(
        ApiMap<UserIdentity, string> identities,
        ApiMap<string, string> claims)
        => new(UserId.Parse(Id), Version) {
            Status = Status,
            Email = Email,
            IsEmailVerified = IsEmailVerified,
            Phone = !Phone.IsNullOrEmpty() ? ActualChat.Phone.Parse(Phone) : null,
            SyncContacts = SyncContacts,
            Name = Name,
            Username = Username,
            IsGreetingCompleted = IsGreetingCompleted,
            CreatedAt = CreatedAt,
            TimeZone = TimeZone,
            AliasId = AliasId.IsNullOrEmpty() ? null : ActualChat.AliasId.Parse(AliasId),
            Identities = identities,
            Claims = claims,
        };

    public void UpdateFrom(AccountFull model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireSomeVersion();

        var name = model.Name;
        Id = id.Value;
        Version = model.Version;
        Status = model.Status;
        Phone = model.Phone?.Value ?? "";
        SyncContacts = model.SyncContacts;
        Email = model.Email;
        IsEmailVerified = model.IsEmailVerified;
        Name = name;
        Username = model.Username;
        IsGreetingCompleted = model.IsGreetingCompleted;
        CreatedAt = model.CreatedAt;
        TimeZone = model.TimeZone;
        AliasId = model.AliasId?.NormalizedValue ?? "";
        if (!model.Username.IsNullOrEmpty())
            UsernameNormalized = model.Username.ToUpper(CultureInfo.InvariantCulture);
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbAccount>
    {
        public void Configure(EntityTypeBuilder<DbAccount> builder) {
            builder.Property(a => a.Id).IsRequired();
            builder.HasIndex(a => a.UsernameNormalized)
                .HasFilter("username_normalized is not null")
                .IsUnique();
            builder.HasIndex(a => new { a.Id, a.TimeZone });
            builder.HasIndex(a => new { a.AliasId })
                .HasFilter("alias_id <> ''")
                .IsUnique();
        }
    }
}
