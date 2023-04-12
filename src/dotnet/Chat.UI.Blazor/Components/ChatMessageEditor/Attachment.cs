namespace ActualChat.Chat.UI.Blazor.Components;

public record Attachment(ChatId ChatId, int Id, string Url, string FileName, string FileType, int Length)
{
    public int Progress { get; init; }
    public MediaId MediaId { get; init; }
    public bool IsImage => FileType.OrdinalIgnoreCaseStartsWith("image");
    public bool Uploaded => Progress == 100;

    public Attachment WithProgress(int value)
        => this with { Progress = value };

    public Attachment WithMediaId(MediaId value)
        => this with { MediaId = value };
}
