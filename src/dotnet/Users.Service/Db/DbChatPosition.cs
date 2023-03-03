using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Users.Db;

[Table("ChatPositions")]
public class DbChatPosition : IHasId<string>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    public ChatPositionKind Kind { get; set; }
    public long EntryLid { get; set; }
    public string Origin { get; set; } = "";

    public static string ComposeId(UserId userId, ChatId chatId, ChatPositionKind kind)
        => $"{userId} {chatId}:{kind.Format()}";

    public ChatPosition ToModel()
        => new(EntryLid, Origin);

    public void UpdateFrom(ChatPosition model)
    {
        EntryLid = model.EntryLid;
        Origin = model.Origin;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatPosition>
    {
        public void Configure(EntityTypeBuilder<DbChatPosition> builder)
            => builder.Property(a => a.Id).IsRequired();
    }
}
