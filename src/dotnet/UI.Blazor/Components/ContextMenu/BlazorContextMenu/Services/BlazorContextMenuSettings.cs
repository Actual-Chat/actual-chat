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

public class BlazorContextMenuDefaultCssSettings
{
    /// <summary>
    /// Allows you to override the default css class of the <see cref="ContextMenu"/>'s div element, for full customization.
    /// </summary>
    public string MenuClass { get; set; } = "blazor-context-menu--default";

    /// <summary>
    /// Allows you to override the default css class of the <see cref="ContextMenu"/>'s div element while it's shown, for full customization.
    /// </summary>
    public string MenuShownClass { get; set; } = "";

    /// <summary>
    /// Allows you to override the default css class of the <see cref="ContextMenu"/>'s div element while it's hidden, for full customization.
    /// </summary>
    public string MenuHiddenClass { get; set; } = "blazor-context-menu--hidden";

    /// <summary>
    /// Allows you to override the default css class of the <see cref="ContextMenu"/>'s ul element, for full customization.
    /// </summary>
    public string MenuListClass { get; set; } = "blazor-context-menu__list";

    /// <summary>
    /// Allows you to override the default css class of the menu <see cref="Item"/>'s li element, for full customization.
    /// </summary>
    public string MenuItemClass { get; set; } = "blazor-context-menu__item--default";

    /// <summary>
    /// Allows you to override the default css class of the menu <see cref="Item"/>'s li element when it contains a <see cref="SubMenu"/>, for full customization.
    /// </summary>
    public string MenuItemWithSubMenuClass { get; set; } = "blazor-context-menu__item--with-submenu";

    /// <summary>
    /// Allows you to override the default css class of the menu <see cref="Item"/>'s li element when disabled, for full customization.
    /// </summary>
    public string MenuItemDisabledClass { get; set; } = "blazor-context-menu__item--default-disabled";

    /// <summary>
    /// Allows you to override the default css class of the menu <see cref="Separator"/>'s li element, for full customization.
    /// </summary>
    public string SeparatorClass { get; set; } = "blazor-context-menu__Separator";

    /// <summary>
    /// Allows you to override the default css class of the menu <see cref="Separator"/>'s hr element, for full customization.
    /// </summary>
    public string SeparatorHrClass { get; set; } = "blazor-context-menu__Separator__hr";
}

public class BlazorContextMenuTemplate
{
    /// <summary>
    /// Additional css class that is applied to the <see cref="ContextMenu"/>'s div element. Use this to extend the default css.
    /// </summary>
    public string MenuClass { get; set; } = "";

    /// <summary>
    /// Additional css class that is applied to the <see cref="ContextMenu"/>'s ul element. Use this to extend the default css.
    /// </summary>
    public string MenuListClass { get; set; } = "";

    /// <summary>
    /// Additional css class for the menu <see cref="Item"/>'s li element. Use this to extend the default css.
    /// </summary>
    public string MenuItemClass { get; set; } = "";

    /// <summary>
    /// Additional css class that is applied to the <see cref="ContextMenu"/>'s div element while it's shown. Use this to extend the default css.
    /// </summary>
    public string MenuShownClass { get; set; } = "";

    /// <summary>
    /// Additional css class that is applied to the <see cref="ContextMenu"/>'s div element while it's hidden. Use this to extend the default css.
    /// </summary>
    public string MenuHiddenClass { get; set; } = "";

    /// <summary>
    /// Additional css class for the menu <see cref="Item"/>'s li element when it contains a <see cref="SubMenu"/>. Use this to extend the default css.
    /// </summary>
    public string MenuItemWithSubMenuClass { get; set; } = "";

    /// <summary>
    /// Additional css class for the menu <see cref="Item"/>'s li element when disabled. Use this to extend the default css.
    /// </summary>
    public string MenuItemDisabledClass { get; set; } = "";

    /// <summary>
    /// Additional css class for the menu <see cref="Separator"/>'s li element. Use this to extend the default css.
    /// </summary>
    public string SeparatorClass { get; set; } = "";

    /// <summary>
    /// Additional css class for the menu <see cref="Separator"/>'s hr element. Use this to extend the default css.
    /// </summary>
    public string SeparatorHrClass { get; set; } = "";

    /// <summary>
    /// Allows you to override the default x position offset of the submenu (i.e. the distance of the submenu from it's parent menu).
    /// </summary>
    public int SubMenuXPositionPixelsOffset { get; set; } = 4;

    /// <summary>
    /// Allows you to set the <see cref="BlazorContextMenu.Animation" /> used by the <see cref="ContextMenu" />.
    /// </summary>
    public Animation Animation {get; set; }

    /// <summary>
    /// Exposes overrides to the default css classes for complete customization. Only recommended if you cannot
    /// achieve customization otherwise and you must replace the default classes.
    /// </summary>
    public BlazorContextMenuDefaultCssSettings DefaultCssOverrides { get; set; } = new BlazorContextMenuDefaultCssSettings();
}

public enum Animation
{
    None,
    FadeIn,
    Grow,
    Slide,
    Zoom
}
