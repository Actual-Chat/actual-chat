using FluentAssertions.Formatting;

namespace ActualChat.Testing.Host.Assertion;

public class ChatFormatter : IValueFormatter
{
    public bool CanHandle(object value)
        => value is Chat.Chat;

    public void Format(object value, FormattedObjectGraph formattedGraph, FormattingContext context, FormatChild formatChild)
    {
        var chat = (Chat.Chat)value;
        var result = $"{chat.Title} (#{chat.Id})";
        if (context.UseLineBreaks)
            formattedGraph.AddLine(result);
        else
            formattedGraph.AddFragment(result);
    }
}
