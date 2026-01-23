using ActualChat.Users;
using AwesomeAssertions.Formatting;

namespace ActualChat.Testing.Host.Assertion;

#pragma warning disable CS0618 // Type or member is obsolete
public class LegacyUserFormatter : IValueFormatter
{
    public bool CanHandle(object value)
        => value is LegacyUser;

    public void Format(object value, FormattedObjectGraph formattedGraph, FormattingContext context, FormatChild formatChild)
    {
        var user = (LegacyUser)value;
        var result = $"{user.Name} (#{user.Id})";
        if (context.UseLineBreaks)
            formattedGraph.AddLine(result);
        else
            formattedGraph.AddFragment(result);
    }
}
#pragma warning restore CS0618
