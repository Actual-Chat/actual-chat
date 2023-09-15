using FluentAssertions.Formatting;

namespace ActualChat.Testing.Assertion;

public class UserFormatter : IValueFormatter
{
    public bool CanHandle(object value)
        => value is User;

    public void Format(object value, FormattedObjectGraph formattedGraph, FormattingContext context, FormatChild formatChild)
    {
        var user = (User)value;
        var result = $"{user.Name} (#{user.Id})";
        if (context.UseLineBreaks)
            formattedGraph.AddLine(result);
        else
            formattedGraph.AddFragment(result);
    }
}
