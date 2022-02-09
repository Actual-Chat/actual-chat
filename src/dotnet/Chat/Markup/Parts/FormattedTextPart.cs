namespace ActualChat.Chat;

[Flags]
public enum Emphasis { None, Em, Strong }

public class FormattedTextPart : MarkupPart
{
    public Emphasis Emphasis { get; init; }
}
