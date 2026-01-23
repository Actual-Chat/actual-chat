using AwesomeAssertions.Formatting;

namespace ActualChat.Testing.Host.Assertion;

public class AccountFullFormatter : IValueFormatter
{
    public bool CanHandle(object value)
        => value is AccountFull;

    public void Format(object value, FormattedObjectGraph formattedGraph, FormattingContext context, FormatChild formatChild)
    {
        var user = (AccountFull)value;
        var result = $"{user.Name} (#{user.Id})";
        if (context.UseLineBreaks)
            formattedGraph.AddLine(result);
        else
            formattedGraph.AddFragment(result);
    }
}
