using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Chat.Db;

[Table("ChatRoles")]
[Index(nameof(ChatId), nameof(LocalId))]
[Index(nameof(ChatId), nameof(Name))]
public class DbChatRole : IHasId<string>
{
    [Key] public string Id { get; set; } = null!;
    string IHasId<string>.Id => Id;

    public string ChatId { get; set; } = null!;
    public long LocalId { get; set; }

    [ConcurrencyCheck] public long Version { get; set; }
    public string Name { get; set; } = "";
    public string Picture { get; set; } = "";
    public string PrincipalIds { get; set; } = ""; // Space-separated

    public static string ComposeId(string chatId, long localId)
        => $"{chatId}:{localId.ToString(CultureInfo.InvariantCulture)}";

    public ChatRole ToModel()
        => new(Id) {
            Id = Id,
            Version = Version,
            Name = Name,
            Picture = Picture,
            PrincipalIds = PrincipalIds.Split(" ").Select(x => new Symbol(x)).ToImmutableHashSet(),
        };

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatAuthor>
    {
        public void Configure(EntityTypeBuilder<DbChatAuthor> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.ChatId).IsRequired();
            builder.Property(a => a.Name).IsRequired();
        }
    }
}
