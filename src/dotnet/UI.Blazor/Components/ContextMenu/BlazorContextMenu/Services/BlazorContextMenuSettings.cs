namespace BlazorContextMenu;

public class BlazorContextMenuSettings
{
    public const string DefaultTemplateName = "default_{89930AFB-8CC8-4672-80D1-EA8BBE65B52A}";
    public readonly Dictionary<string, BlazorContextMenuTemplate> Templates = new (StringComparer.Ordinal)
    {
        { DefaultTemplateName, new BlazorContextMenuTemplate() }
    };

    public BlazorContextMenuTemplate GetTemplate(string templateName)
    {
        if (!Templates.ContainsKey(templateName)) throw new Exception($"Template '{templateName}' not found");
        return Templates[templateName];
    }
}

public class BlazorContextMenuTemplate
{
    /// <summary>
    /// Base css class that is applied to the <see cref="ContextMenu"/>'s div element.
    /// </summary>
    public string MenuClass { get; set; } = "blazor-context-menu--default";

    /// <summary>
    /// Base css class that is applied to the <see cref="ContextMenu"/>'s ul element.
    /// </summary>
    public string MenuListClass { get; set; } = "blazor-context-menu__list";

    /// <summary>
    /// Base css class for the menu <see cref="Item"/>'s li element.
    /// </summary>
    public string MenuItemClass { get; set; } = "blazor-context-menu__item--default";

    /// <summary>
    /// Base css class that is applied to the <see cref="ContextMenu"/>'s div element while it's shown.
    /// </summary>
    public string MenuShownClass { get; set; } = "";

    /// <summary>
    /// Base css class that is applied to the <see cref="ContextMenu"/>'s div element while it's hidden.
    /// </summary>
    public string MenuHiddenClass { get; set; } = "blazor-context-menu--hidden";

    /// <summary>
    /// Base css class for the menu <see cref="Item"/>'s li element when it contains a <see cref="SubMenu"/>.
    /// </summary>
    public string MenuItemWithSubMenuClass { get; set; } = "blazor-context-menu__item--with-submenu";

    /// <summary>
    /// Base css class for the menu <see cref="Item"/>'s li element when disabled.
    /// </summary>
    public string MenuItemDisabledClass { get; set; } = "blazor-context-menu__item--default-disabled";

    /// <summary>
    /// Base css class for the menu <see cref="Separator"/>'s li element.
    /// </summary>
    public string SeparatorClass { get; set; } = "blazor-context-menu__Separator";

    /// <summary>
    /// Base css class for the menu <see cref="Separator"/>'s hr element.
    /// </summary>
    public string SeparatorHrClass { get; set; } = "blazor-context-menu__Separator__hr";

    /// <summary>
    /// Allows you to override the default x position offset of the submenu (i.e. the distance of the submenu from it's parent menu).
    /// </summary>
    public int SubMenuXPositionPixelsOffset { get; set; } = 4;

    /// <summary>
    /// Allows you to set the <see cref="BlazorContextMenu.Animation" /> used by the <see cref="ContextMenu" />.
    /// </summary>
    public Animation Animation {get; set; }
}

public enum Animation
{
    None,
    FadeIn,
    Grow,
    Slide,
    Zoom
}
