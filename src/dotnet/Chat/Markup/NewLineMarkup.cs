namespace ActualChat.Chat;

public record NewLineMarkup() : TextMarkup("\r\n")
{
    public static NewLineMarkup Instance { get; } = new();

    public override TextMarkupKind Kind => TextMarkupKind.NewLine;
}
