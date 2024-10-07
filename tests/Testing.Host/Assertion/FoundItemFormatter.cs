using ActualChat.UI.Blazor.App.Services;
using FluentAssertions.Formatting;

namespace ActualChat.Testing.Host.Assertion;

public class FoundItemFormatter : IValueFormatter
{
    public bool CanHandle(object value)
        => value is FoundItem;

    public void Format(object value, FormattedObjectGraph formattedGraph, FormattingContext context, FormatChild formatChild)
    {
        var foundContact = (FoundItem)value;
        var scope = foundContact.IsGlobalSearchResult ? "Global" : foundContact.Scope.ToString();
        var result = $"{foundContact.SearchResult.SearchMatch.Text} (#{foundContact.SearchResult.Id}) {scope}";
        if (context.UseLineBreaks)
            formattedGraph.AddLine(result);
        else
            formattedGraph.AddFragment(result);
    }
}
