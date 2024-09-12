using FluentAssertions.Formatting;

// ReSharper disable InconsistentlySynchronizedField

namespace ActualChat.Testing.Host.Assertion;

public static class ActualFluentFormatters
{
    private static readonly Lock Lock = new ();
    private static volatile bool _isUsed;

    public static void Use()
    {
        if (_isUsed)
            return;

        lock (Lock) {
            if (_isUsed)
                return;

            Add<UserFormatter>();
            Add<ChatFormatter>();
            Add<ContactFormatter>();
            Add<ContactSearchResultFormatter>();
            Add<FoundItemFormatter>();
            Add<EntrySearchResultFormatter>();
            _isUsed = true;
        }
    }

    public static void Remove()
    {
        if (!_isUsed)
            return;

        lock (Lock) {
            if (!_isUsed)
                return;

            Remove<UserFormatter>();
            Remove<ChatFormatter>();
            Remove<ContactFormatter>();
            Remove<ContactSearchResultFormatter>();
            Remove<FoundItemFormatter>();
            Remove<EntrySearchResultFormatter>();
            _isUsed = false;
        }
    }

    private static void Add<T>() where T : IValueFormatter, new()
        => FluentAssertions.Formatting.Formatter.AddFormatter(new T());

    private static void Remove<T>() where T : IValueFormatter
    {
        var toRemove = FluentAssertions.Formatting.Formatter.Formatters.OfType<T>().ToList();
        foreach (var formatter in toRemove)
            FluentAssertions.Formatting.Formatter.RemoveFormatter(formatter);
    }
}
