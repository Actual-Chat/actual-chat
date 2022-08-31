namespace BlazorContextMenu.Services;

public interface IContextMenuStorage
{
    ContextMenuBase? GetMenu(string id);
    void Register(ContextMenuBase menu);
    void Unregister(ContextMenuBase menu);
}

public class ContextMenuStorage : IContextMenuStorage
{
    private readonly Dictionary<string, ContextMenuBase> _initializedMenus = new (StringComparer.Ordinal);

    public void Register(ContextMenuBase menu)
        => _initializedMenus[menu.Id] = menu;

    public void Unregister(ContextMenuBase menu)
        => _initializedMenus.Remove(menu.Id);

    public ContextMenuBase? GetMenu(string id)
        => _initializedMenus.TryGetValue(id, out var menu) ? menu : null;
}
