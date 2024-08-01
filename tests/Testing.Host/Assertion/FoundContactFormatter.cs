using ActualChat.Chat.UI.Blazor.Services;
using FluentAssertions.Formatting;

namespace ActualChat.Testing.Host.Assertion;

public class FoundContactFormatter : IValueFormatter
{
    public bool CanHandle(object value)
        => value is FoundContact;

    public void Format(object value, FormattedObjectGraph formattedGraph, FormattingContext context, FormatChild formatChild)
    {
        var foundContact = (FoundContact)value;
        var result = $"{foundContact.SearchResult.SearchMatch.Text} (#{foundContact.SearchResult.Id})";
        if (context.UseLineBreaks)
            formattedGraph.AddLine(result);
        else
            formattedGraph.AddFragment(result);
    }
}
