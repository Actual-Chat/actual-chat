namespace ActualChat.Testing.Assertion;

public static class ActualFluentFormatters
{
    public static void Use()
    {
        FluentAssertions.Formatting.Formatter.AddFormatter(new UserFormatter());
        FluentAssertions.Formatting.Formatter.AddFormatter(new ContactFormatter());
    }

    public static void Remove()
    {
        foreach (var formatter in FluentAssertions.Formatting.Formatter.Formatters.OfType<UserFormatter>().ToList())
            FluentAssertions.Formatting.Formatter.RemoveFormatter(formatter);
        foreach (var formatter in FluentAssertions.Formatting.Formatter.Formatters.OfType<ContactFormatter>().ToList())
            FluentAssertions.Formatting.Formatter.RemoveFormatter(formatter);
    }
}
