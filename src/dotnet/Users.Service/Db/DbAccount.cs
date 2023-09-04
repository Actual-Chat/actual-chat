using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Users.Db;

public class DbAccount : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    [Column(TypeName = "smallint")]
    public AccountStatus Status { get; set; }
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool SyncContacts { get; set; }
    public string Name { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Username { get; set; } = "";
    public string? UsernameNormalized { get; set; }
    public bool IsGreetingCompleted { get; set; }

    public AccountFull ToModel(User user)
    {
        if (user.Id != Id)
            throw new ArgumentOutOfRangeException(nameof(user));

        return new(user, Version) {
            Status = Status,
            Email = Email,
            Phone = new Phone(Phone),
            SyncContacts = SyncContacts,
            Name = Name,
            LastName = LastName,
            Username = Username,
            IsGreetingCompleted = IsGreetingCompleted,
        };
    }

    public void UpdateFrom(AccountFull model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        Status = model.Status;
        Phone = model.Phone;
        SyncContacts = model.SyncContacts;
        Email = model.Email;
        Name = model.Name;
        LastName = model.LastName;
        Username = model.Username;
        IsGreetingCompleted = model.IsGreetingCompleted;
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
        }
    }
}
