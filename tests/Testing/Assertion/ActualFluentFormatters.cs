using FluentAssertions.Formatting;

namespace ActualChat.Testing.Assertion;

public static class ActualFluentFormatters
{
    public static void Use()
    {
        AddRemove<UserFormatter>();
        AddRemove<ContactFormatter>();
        AddRemove<ContactSearchResultFormatter>();
    }

    public static void Remove()
    {
        Remove<UserFormatter>();
        Remove<ContactFormatter>();
        Remove<ContactSearchResultFormatter>();
    }

    private static void AddRemove<T>() where T : IValueFormatter, new()
        => FluentAssertions.Formatting.Formatter.AddFormatter(new T());

    private static void Remove<T>() where T : IValueFormatter
    {
        var toRemove = FluentAssertions.Formatting.Formatter.Formatters.OfType<T>().ToList();
        foreach (var formatter in toRemove)
            FluentAssertions.Formatting.Formatter.RemoveFormatter(formatter);
    }
}
