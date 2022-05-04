namespace ActualChat.Chat.UI.Blazor.Components;

public class Attachment
{
    public int Id { get; init; }
    public string Url { get; init; } = "";
    public string FileName { get; init; } = "";
    public string FileType { get; init; } = "";
    public int Length { get; init; }
    public bool IsImage => FileType?.StartsWith("image", StringComparison.OrdinalIgnoreCase) ?? false;
}
