using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("ChatImports")]
public sealed class DbChatImport
{
    [DbKey] public string Id { get; set; } = "";
    public string ImportId { get; set; } = "";
    public string StartedBy { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public bool IsActive { get; set; }

    public ChatImportSession ToModel()
        => new(ChatId.Parse(Id), ImportId, UserId.Parse(StartedBy),
            StartedAt.DefaultKind(DateTimeKind.Utc), IsActive);
}

[Table("ChatImportConsents")]
[Index(nameof(ImportId))]
public sealed class DbChatImportConsent
{
    [DbKey] public string Id { get; set; } = "";
    public string ImportId { get; set; } = "";
    public string UserId { get; set; } = "";
    public bool HasConsent { get; set; }
}

[Table("ChatImportBatches")]
public sealed class DbChatImportBatch
{
    [DbKey] public string Id { get; set; } = "";
    public string Request { get; set; } = "";
    public string Result { get; set; } = "";
}

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
        => new(ActualChat.ChatId.Parse(ChatId), ImportId, UploadId.Parse(Id),
            ActualChat.UserId.Parse(UserId), ActualChat.UserId.Parse(UploadedBy),
            MediaJson.IsNullOrEmpty() ? null : JsonSerializer.Deserialize<MediaRef>(MediaJson));
}
