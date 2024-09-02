using ActualChat.Search;
using FluentAssertions.Formatting;

namespace ActualChat.Testing.Host.Assertion;

public class EntrySearchResultFormatter : IValueFormatter
{
    public bool CanHandle(object value)
        => value is EntrySearchResult;

    public void Format(object value, FormattedObjectGraph formattedGraph, FormattingContext context, FormatChild formatChild)
    {
        var item = (EntrySearchResult)value;
        var result = $"{item.Text} (#{item.Id}) {FormatSearchMatch(item.SearchMatch)}";
        if (context.UseLineBreaks)
            formattedGraph.AddLine(result);
        else
            formattedGraph.AddFragment(result);
    }

    private static string FormatSearchMatch(SearchMatch searchMatch)
        => '[' + string.Join(", ", searchMatch.Parts.Select(x => $"{x.Range.Start}:{x.Range.End}")) + ']';
}
