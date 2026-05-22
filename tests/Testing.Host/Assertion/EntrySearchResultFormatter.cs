using ActualChat.Search;
using AwesomeAssertions.Formatting;

namespace ActualChat.Testing.Host.Assertion;

public class EntrySearchResultFormatter : IValueFormatter
{
    public bool CanHandle(object value)
        => value is FoundChatEntry;

    public void Format(object value, FormattedObjectGraph formattedGraph, FormattingContext context, FormatChild formatChild)
    {
        var item = (FoundChatEntry)value;
        var result = $"{item.Match.Text} (#{item.EntryId}) {FormatSearchMatch(item.Match)}";
        if (context.UseLineBreaks)
            formattedGraph.AddLine(result);
        else
            formattedGraph.AddFragment(result);
    }

    private static string FormatSearchMatch(SearchMatch searchMatch)
        => '[' + string.Join(", ", searchMatch.Parts.Select(x => $"{x.Range.Start}:{x.Range.End}")) + ']';
}
