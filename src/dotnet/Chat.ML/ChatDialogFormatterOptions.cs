namespace ActualChat.Chat.ML;

public record ChatDialogFormatterOptions
{
    public static readonly ChatDialogFormatterOptions Default = new ();

    public bool DisplayAuthorPerEntry { get; init; }
    public bool DisplayTimestamp { get; init; }
    public bool UseSquareBracketsFormat { get; init; }
}
