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
public class DbChatRoulette : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = null!;
    public string ProfileId1 { get; set; } = null!;
    public string ProfileId2 { get; set; } = null!;
    public string UserId1 { get; set; } = null!;
    public string UserId2 { get; set; } = null!;

    public DbChatRoulette() { }
    public DbChatRoulette(ChatRouletteFull model) => UpdateFrom(model);

    private void UpdateFrom(ChatRouletteFull model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        ChatId = model.ChatId;
        ProfileId1 = model.ProfileId1;
        ProfileId2 = model.ProfileId2;
        UserId1 = model.UserId1;
        UserId2 = model.UserId2;
    }

    public ChatRouletteFull ToModel()
        => new (new ChatRouletteId(Id), Version) {
            ChatId = new ChatId(ChatId),
            UserId1 = new UserId(UserId1),
            UserId2 = new UserId(UserId2),
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
        }
    }
}
