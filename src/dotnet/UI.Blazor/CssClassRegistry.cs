using System.Collections.Concurrent;

namespace ActualChat.UI.Blazor;

public static class CssClassRegistry
{
    private static readonly ConcurrentDictionary<Type, string> CssClasses = new();

    public static Func<Type, string> DefaultCssClassGenerator =
        type => type.Name.ToSentenceCase("-").ToLowerInvariant();

    public static void Add(Type type, string cssClass)
        => CssClasses[type] = cssClass;

    public static string Get(Type type)
        => CssClasses.GetOrAdd(type, DefaultCssClassGenerator);
}
