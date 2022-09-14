using ActualChat.UI.Blazor.Module;
using BlazorContextMenu.Services;

namespace BlazorContextMenu;

public interface IBlazorContextMenuService
{
    /// <summary>
    /// Hides a <see cref="ContextMenu" /> programmatically.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    Task HideMenu(string id);

    /// <summary>
    /// Shows a <see cref="ContextMenu" /> programmatically.
    /// </summary>
    /// <param name="id">The id of the <see cref="ContextMenu"/>.</param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    Task ShowMenu(string id, int x, int y);

    /// <summary>
    /// Shows a <see cref="ContextMenu" /> programmatically.
    /// </summary>
    /// <param name="id">The id of the <see cref="ContextMenu"/>.</param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="data">Extra data that will be passed to menu events.</param>
    /// <returns></returns>
    Task ShowMenu(string id, int x, int y, object? data);
}

public class BlazorContextMenuService : IBlazorContextMenuService
{
    private IJSRuntime JS { get; }
    private IContextMenuStorage ContextMenuStorage { get; }

    public BlazorContextMenuService(IJSRuntime jSRuntime, IContextMenuStorage contextMenuStorage)
    {
        JS = jSRuntime;
        ContextMenuStorage = contextMenuStorage;
    }

    public async Task HideMenu(string id)
    {
        var menu = ContextMenuStorage.GetMenu(id);
        if (menu == null)
            throw new Exception($"No context menu with id '{id}' was found");
        await JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.blazorContextMenu.Hide", id);
    }

    public async Task ShowMenu(string id, int x, int y, object? data)
    {
        var menu = ContextMenuStorage.GetMenu(id);
        if(menu == null)
            throw new Exception($"No context menu with id '{id}' was found");
        menu.Data = data;
        await JS.InvokeVoidAsync($"{BlazorUICoreModule.ImportName}.blazorContextMenu.ManualShow", id, x, y);
    }

    public Task ShowMenu(string id, int x, int y)
        => ShowMenu(id, x, y, null);
}
