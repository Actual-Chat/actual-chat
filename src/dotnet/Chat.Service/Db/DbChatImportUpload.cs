using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Chat.Db;

[Table("ChatImportUploads")]
public sealed class DbChatImportUpload
{
    [DbKey] public string Id { get; set; } = "";
    public string ChatId { get; set; } = "";
    public string ImportId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string UploadedBy { get; set; } = "";
    public string MediaJson { get; set; } = "";
    public string? EntryId { get; set; }

    public ChatImportUpload ToModel()
        => new(ActualChat.ChatId.Parse(ChatId), ChatImportId.Parse(ImportId), UploadId.Parse(Id),
            ActualChat.UserId.Parse(UserId), ActualChat.UserId.Parse(UploadedBy),
            MediaJson.IsNullOrEmpty() ? null : JsonSerializer.Deserialize<MediaRef>(MediaJson));
}
