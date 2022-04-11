using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Users.Db;

[Table("ChatReadPositions")]
public class DbChatReadPosition
{
    public string UserId { get; set; } = null!;
    public string ChatId { get; set; } = null!;
    public long EntryId { get; set; }
}
