namespace ActualChat.Chat;

public record NewLineMarkup() : TextMarkup("\r\n")
{
    public static readonly NewLineMarkup Instance = new();

    public override TextMarkupKind Kind => TextMarkupKind.NewLine;
}
