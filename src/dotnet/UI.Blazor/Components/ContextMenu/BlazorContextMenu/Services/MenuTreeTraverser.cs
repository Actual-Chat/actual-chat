namespace BlazorContextMenu.Services;

public interface IMenuTreeTraverser
{
    ContextMenuBase? GetClosestContextMenu(MenuTreeComponent menuTreeComponent);
    ContextMenu? GetRootContextMenu(MenuTreeComponent menuTreeComponent);
    bool HasSubMenu(MenuTreeComponent menuTreeComponent);
}

public class MenuTreeTraverser : IMenuTreeTraverser
{
    public ContextMenu? GetRootContextMenu(MenuTreeComponent menuTreeComponent)
    {
        if (menuTreeComponent.ParentComponent == null) return null;
        if (menuTreeComponent.ParentComponent is ContextMenu contextMenu)
            return contextMenu;
        return GetRootContextMenu(menuTreeComponent.ParentComponent);
    }

    public ContextMenuBase? GetClosestContextMenu(MenuTreeComponent menuTreeComponent)
    {
        var parentComponent = menuTreeComponent.ParentComponent;
        if (parentComponent == null) return null;
        if (menuTreeComponent.ParentComponent is ContextMenuBase contextMenuBase)
            return contextMenuBase;
        return GetClosestContextMenu(parentComponent);
    }

    public bool HasSubMenu(MenuTreeComponent menuTreeComponent)
    {
        var children = menuTreeComponent.GetChildComponents();
        if (children.Any(x => x is SubMenu)) return true;
        foreach (var child in children)
            if (HasSubMenu(child)) return true;
        return false;
    }
}
