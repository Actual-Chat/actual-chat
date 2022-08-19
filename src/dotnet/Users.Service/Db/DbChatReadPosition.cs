using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Users.Db;

[Table("ChatReadPositions")]
public class DbChatReadPosition : IHasId<string>, IRequirementTarget
{
    [Key] public string Id { get; set; } = "";
    public long ReadEntryId { get; set; }

    public static string ComposeId(string userId, string chatId)
        => $"{userId} {chatId}";
}
