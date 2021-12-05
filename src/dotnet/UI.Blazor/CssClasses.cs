using System.Collections.Concurrent;

namespace ActualChat.UI.Blazor;

public class CssClasses
{
    public static Func<Type, string> DefaultGenerator { get; set; } =
        type => type.Name.ToSentenceCase("-").ToLowerInvariant();
    public static CssClasses Default { get; set; } = new();

    private readonly ConcurrentDictionary<Type, string> _known = new();
    private readonly Func<Type, string> _generator;

    public CssClasses(Func<Type, string>? generator = null)
        => _generator = generator ?? DefaultGenerator;

    public string this[Type type] {
        get => _known.GetOrAdd(type, _generator);
        set => _known[type] = value;
    }
}
