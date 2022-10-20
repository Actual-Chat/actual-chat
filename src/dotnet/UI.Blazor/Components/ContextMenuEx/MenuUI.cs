namespace ActualChat.UI.Blazor.Components;

public static class MenuUI
{
    private static volatile ConcurrentDictionary<string, Type> _menus = new (StringComparer.Ordinal);

    public static void Register<TMenu>() where TMenu: ContextMenuExBase
        => _menus.AddOrUpdate(typeof(TMenu).Name, typeof(TMenu), (_, _) => typeof(TMenu));

    public static Type Get(string menu)
    {
        if (_menus.TryGetValue(menu, out var type))
            return type;
        throw new KeyNotFoundException(menu);
    }
}
