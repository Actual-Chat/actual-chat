using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Chat.Db;

[Table("ChatImportBatches")]
public sealed class DbChatImportBatch
{
    [DbKey] public string Id { get; set; } = "";
    public string Request { get; set; } = "";
    public string Result { get; set; } = "";
}
