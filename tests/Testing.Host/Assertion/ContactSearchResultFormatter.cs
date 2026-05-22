using ActualChat.Search;
using AwesomeAssertions.Formatting;

namespace ActualChat.Testing.Host.Assertion;

public class ContactSearchResultFormatter : IValueFormatter
{
    public bool CanHandle(object value)
        => value is FoundContact;

    public void Format(object value, FormattedObjectGraph formattedGraph, FormattingContext context, FormatChild formatChild)
    {
        var item = (FoundContact)value;
        var result = $"{item.Match.Text} (#{item.ContactId}) {FormatSearchMatch(item.Match)}";
        if (context.UseLineBreaks)
            formattedGraph.AddLine(result);
        else
            formattedGraph.AddFragment(result);
    }

    private static string FormatSearchMatch(SearchMatch searchMatch)
        => '[' + string.Join(", ", searchMatch.Parts.Select(x => $"{x.Range.Start}:{x.Range.End}")) + ']';
}
