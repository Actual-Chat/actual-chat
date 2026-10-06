using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("ChatImportConsents")]
[Index(nameof(ImportId))]
public sealed class DbChatImportConsent
{
    [DbKey] public string Id { get; set; } = "";
    public string ImportId { get; set; } = "";
    public string UserId { get; set; } = "";
    public bool HasConsent { get; set; }
}
