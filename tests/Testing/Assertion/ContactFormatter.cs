using ActualChat.Contacts;
using FluentAssertions.Formatting;

namespace ActualChat.Testing.Assertion;

public class ContactFormatter : IValueFormatter
{
    public bool CanHandle(object value)
        => value is Contact;

    public void Format(object value, FormattedObjectGraph formattedGraph, FormattingContext context, FormatChild formatChild)
    {
        var contact = (Contact)value;
        var result = $"{contact.Chat.Title} (#{contact.Id})";
        if (context.UseLineBreaks)
            formattedGraph.AddLine(result);
        else
            formattedGraph.AddFragment(result);
    }
}
