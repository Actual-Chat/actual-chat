namespace ActualChat.Chat;

public class Markup
{
    public string Text { get; init; } = "";
    public MarkupParts.Part[] Parts { get; init; } = Array.Empty<MarkupParts.Part>();

    public Markup() { }
    public Markup(string text, MarkupParts.Part[] parts)
    {
        Text = text;
        Parts = parts;
    }

    public override string ToString()
    {
        var sParts = Parts.Select(e => e.ToString()).ToDelimitedString($",{Environment.NewLine}  ");
        if (!sParts.IsNullOrEmpty())
            sParts = $",{Environment.NewLine}  {sParts}";
        return $"{GetType().Name}({SystemJsonSerializer.Default.Write(Text)}{sParts})";
    }
}
