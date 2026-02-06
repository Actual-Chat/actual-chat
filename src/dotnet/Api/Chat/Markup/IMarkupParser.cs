namespace ActualChat.Chat;

/// <summary>
/// Parses text into markup elements.
/// </summary>
public interface IMarkupParser
{
    Markup Parse(string text);
}
