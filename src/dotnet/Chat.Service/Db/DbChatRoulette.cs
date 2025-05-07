using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Roulette;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("ChatRoulettes")]
[Index(nameof(ProfileId1), nameof(ProfileId2), IsUnique = true)]
[Index(nameof(ChatId), IsUnique = true)]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbChatRoulette : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = null!;
    public string ProfileId1 { get; set; } = null!;
    public string ProfileId2 { get; set; } = null!;
    public string UserId1 { get; set; } = null!;
    public string UserId2 { get; set; } = null!;
    public string InitiatedByUserId { get; set; } = null!;
    public string CompletedByUserId { get; set; } = null!;
    public CompleteChatRouletteReason CompleteReason { get; set; } = CompleteChatRouletteReason.None;
    public DateTime CompletedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbChatRoulette() { }
    public DbChatRoulette(ChatRouletteFull model) => UpdateFrom(model);

    public void UpdateFrom(ChatRouletteFull model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireSomeVersion();

        Id = id.Value;
        Version = model.Version;
        ChatId = model.ChatId.Value;
        ProfileId1 = model.ProfileId1;
        ProfileId2 = model.ProfileId2;
        UserId1 = model.UserId1.Value;
        UserId2 = model.UserId2.Value;
        InitiatedByUserId = model.InitiatedBy.Value;
        CompletedByUserId = model.CompletedBy?.Value ?? "";
        CompleteReason = model.CompleteReason;
        CompletedAt = model.CompletedAt;
    }

    public ChatRouletteFull ToModel()
        => new (new ChatRouletteId(Id), Version) {
            ChatId = ActualChat.ChatId.Parse(ChatId),
            UserId1 = UserId.Parse(UserId1),
            UserId2 = UserId.Parse(UserId2),
            InitiatedBy = UserId.Parse(InitiatedByUserId),
            CompletedBy = UserId.ParseNullable(CompletedByUserId),
            CompleteReason = CompleteReason,
            CompletedAt = CompletedAt,
        };

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatRoulette>
    {
        public void Configure(EntityTypeBuilder<DbChatRoulette> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.ChatId).IsRequired();
            builder.Property(a => a.ProfileId1).IsRequired();
            builder.Property(a => a.ProfileId2).IsRequired();
            builder.Property(a => a.UserId1).IsRequired();
            builder.Property(a => a.UserId2).IsRequired();
            builder.Property(a => a.InitiatedByUserId).IsRequired();
            builder.Property(a => a.CompletedByUserId).IsRequired();
        }
    }
}
