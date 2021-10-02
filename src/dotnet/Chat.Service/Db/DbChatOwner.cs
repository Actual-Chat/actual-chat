using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Chat.Db
{
    [Table("ChatOwners")]
    public class DbChatOwner
    {
        public string ChatId { get; set; } = "";
        public string UserId { get; set; } = "";
    }
}
