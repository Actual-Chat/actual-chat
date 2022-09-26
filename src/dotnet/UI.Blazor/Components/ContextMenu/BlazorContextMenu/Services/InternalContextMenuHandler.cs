using BlazorContextMenu.Services;
namespace BlazorContextMenu;

public interface IInternalContextMenuHandler
{
    bool ReferencePassedToJs { get; set; }
    Task<bool> HideMenu(string id);
    Task ShowMenu(string id, string x, string y, string targetId, string triggerRef);
}

internal class InternalContextMenuHandler : IInternalContextMenuHandler
{
    private readonly IContextMenuStorage _contextMenuStorage;
    private readonly ContextMenuTriggerStorage _triggerStorage;

    public InternalContextMenuHandler(IContextMenuStorage contextMenuStorage, ContextMenuTriggerStorage triggerStorage)
    {
        _contextMenuStorage = contextMenuStorage;
        _triggerStorage = triggerStorage;
    }

    public bool ReferencePassedToJs { get; set; } = false;

    /// <summary>
    /// Shows the context menu at the specified coordinates.
    /// </summary>
    /// <param name="id">The id of the menu.</param>
    /// <param name="x">The x coordinate on the screen.</param>
    /// <param name="y">The y coordinate on the screen.</param>
    /// <param name="targetId">The id of the element that triggered the menu show event.</param>
    /// <param name="triggerRef">The reference to <see cref="ContextMenuTrigger"/> that opened the menu.</param>
    /// <returns></returns>
    [JSInvokable]
    public async Task ShowMenu(string id, string x, string y, string targetId, string triggerRef)
    {
        var menu = _contextMenuStorage.GetMenu(id);
        if (menu == null)
            return;
        var trigger = !triggerRef.IsNullOrEmpty() ? _triggerStorage.GetTrigger(triggerRef) : null;
        await menu.Show(x, y, targetId, trigger);
    }

    /// <summary>
    /// Hides a context menu
    /// </summary>
    /// <param name="id">The id of the menu.</param>
    [JSInvokable]
    public async Task<bool> HideMenu(string id)
    {
        var menu = _contextMenuStorage.GetMenu(id);
        if (menu != null)
            return await menu.Hide();
        return true;
    }
}
