namespace ActualChat.UI.Blazor.Components;

public static class MenuRegistry
{
    private static volatile ConcurrentDictionary<string, Type> _menus = new (StringComparer.Ordinal);

    public static void Register<TMenu>()
    {
        if (!_menus.TryAdd(typeof(TMenu).Name, typeof(TMenu)))
            throw new ArgumentOutOfRangeException(nameof(TMenu), typeof(TMenu).Name, null);
    }

    public static Type Resolve(string menu)
    {
        if (_menus.TryGetValue(menu, out var type))
            return type;
        throw new KeyNotFoundException(menu);
    }
}
