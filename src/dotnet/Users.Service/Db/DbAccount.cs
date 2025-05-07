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
    [Column("UserLinkId")] // TODO(AY): Rename to AliasId
    public string AliasId { get; set; } = "";
    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public AccountFull ToModel(User user)
    {
        if (!OrdinalEquals(user.Id, Id))
            throw new ArgumentOutOfRangeException(nameof(user));

        return new(user, Version) {
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
        };
    }

    public void UpdateFrom(AccountFull model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireSomeVersion();

        var name = model.Name;
 #pragma warning disable CS0618 // Type or member is obsolete
        if (!model.LastName.IsNullOrEmpty())
            name = $"{name} {model.LastName}";
#pragma warning restore CS0618 // Type or member is obsolete
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
                .HasFilter("user_link_id <> ''") // TODO(AY): Rename to alias_id
                .IsUnique();
        }
    }
}
