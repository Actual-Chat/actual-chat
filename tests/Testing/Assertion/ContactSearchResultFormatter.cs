using ActualChat.Search;
using FluentAssertions.Formatting;

namespace ActualChat.Testing.Assertion;

public class ContactSearchResultFormatter : IValueFormatter
{
    public bool CanHandle(object value)
        => value is ContactSearchResult;

    public void Format(object value, FormattedObjectGraph formattedGraph, FormattingContext context, FormatChild formatChild)
    {
        var item = (ContactSearchResult)value;
        var result = $"{item.Text} (#{item.Id})";
        if (context.UseLineBreaks)
            formattedGraph.AddLine(result);
        else
            formattedGraph.AddFragment(result);
    }
}
