using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Chat.Db;

[Table("InviteCodes")]
public class DbInviteCode : IHasId<string>, IHasVersion<long>
{
    private DateTime _createdAt;
    private DateTime _expiresOn;

    public DbInviteCode() { }
    public DbInviteCode(InviteCode model) => UpdateFrom(model);

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = "";

    public string CreatedBy { get; set; } = "";

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime ExpiresOn {
        get => _expiresOn.DefaultKind(DateTimeKind.Utc);
        set => _expiresOn = value.DefaultKind(DateTimeKind.Utc);
    }

    public InviteCodeState State { get; set; }

    public string Value { get; set; } = "";

    public InviteCode ToModel()
        => new() {
            Id = Id,
            Version = Version,
            ChatId = ChatId,
            CreatedBy = CreatedBy,
            CreatedAt = CreatedAt,
            ExpiresOn = ExpiresOn,
            State = State,
            Value = Value
        };

    public void UpdateFrom(InviteCode model)
    {
        Id = model.Id;
        Version = model.Version;
        ChatId = model.ChatId;
        CreatedBy = model.CreatedBy;
        CreatedAt = model.CreatedAt;
        ExpiresOn = model.ExpiresOn;
        State = model.State;
        Value = model.Value;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbInviteCode>
    {
        public void Configure(EntityTypeBuilder<DbInviteCode> builder)
        {
            builder.Property(a => a.Id).ValueGeneratedOnAdd().HasValueGenerator<UlidValueGenerator>();
            builder.HasIndex(a => a.Value).IsUnique().HasFilter($"\"{nameof(State)}\" = 0");
        }
    }
}
