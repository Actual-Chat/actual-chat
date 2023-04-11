namespace ActualChat.Chat.UI.Blazor.Components;

public record Attachment(int Id, string Url, string FileName, string FileType, int Length)
{
    public int Progress { get; init; }
    public bool IsImage => FileType.OrdinalIgnoreCaseStartsWith("image");
    public bool Uploaded => Progress == 100;

    public Attachment WithProgress(int value)
        => this with { Progress = value };
}
