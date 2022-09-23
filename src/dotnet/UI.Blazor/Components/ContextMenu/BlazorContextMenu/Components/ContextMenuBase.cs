using ActualChat.UI.Blazor.Module;
using BlazorContextMenu.Services;

namespace BlazorContextMenu;

public abstract class ContextMenuBase : MenuTreeComponent
{
    [Inject] private BlazorContextMenuSettings Settings { get; init; } = null!;
    [Inject] private IContextMenuStorage ContextMenuStorage { get; init; } = null!;
    [Inject] private IInternalContextMenuHandler ContextMenuHandler { get; init; } = null!;
    [Inject] private IJSRuntime JS { get; init; } = null!;
    [Inject] private IMenuTreeTraverser MenuTreeTraverser { get; init; } = null!;

    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object> Attributes { get; set; } = null!;

    protected virtual string BaseClass => "blazor-context-menu blazor-context-menu__wrapper";

    /// <summary>
    /// The id that the <see cref="ContextMenuTrigger" /> will use to bind to. This parameter is required
    /// </summary>
    [Parameter]
    public string Id { get; set; } = "";

    /// <summary>
    /// The name of the template to use for this <see cref="ContextMenu" /> and all its <see cref="SubMenu" />.
    /// </summary>
    [Parameter]
    public string? Template { get; set; }

    [CascadingParameter(Name = "CascadingTemplate")]
    protected string? CascadingTemplate { get; set; }

    /// <summary>
    /// Additional css class that is applied to the <see cref="ContextMenu"/>'s div element. Use this to extend the default css.
    /// </summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>
    /// Additional css class that is applied to the <see cref="ContextMenu"/>'s div element while is shown. Use this to extend the default css.
    /// </summary>
    [Parameter]
    public string? ShownClass { get; set; }

    /// <summary>
    /// Additional css class that is applied to the <see cref="ContextMenu"/>'s div element while is hidden. Use this to extend the default css.
    /// </summary>
    [Parameter]
    public string? HiddenClass { get; set; }

    /// <summary>
    /// Additional css class that is applied to the <see cref="ContextMenu"/>'s ul element. Use this to extend the default css.
    /// </summary>
    [Parameter]
    public string? ListClass { get; set; }

    /// <summary>
    /// Allows you to set the <see cref="BlazorContextMenu.Animation" /> used by this <see cref="ContextMenu" /> and all its <see cref="SubMenu" />
    /// </summary>
    [Parameter]
    public Animation? Animation { get; set; }

    /// <summary>
    /// A handler that is triggered before the menu appears. Can be used to prevent the menu from showing.
    /// </summary>
    [Parameter]
    public EventCallback<MenuAppearingEventArgs> OnAppearing { get; set; }

    /// <summary>
    /// A handler that is triggered before the menu is hidden. Can be used to prevent the menu from hiding.
    /// </summary>
    [Parameter]
    public EventCallback<MenuHidingEventArgs> OnHiding { get; set; }

    /// <summary>
    /// Set to false if you want to close the menu programmatically. Default: true
    /// </summary>
    [Parameter]
    public bool AutoHide { get; set; } = true;

    /// <summary>
    /// Set to AutoHideEvent.MouseUp if you want it to close the menu on the MouseUp event. Default: AutoHideEvent.MouseDown
    /// </summary>
    [Parameter]
    public AutoHideEvent AutoHideEvent { get; set; } = AutoHideEvent.MouseDown;

    /// <summary>
    /// Set CSS z-index for overlapping other html elements. Default: 1000
    /// </summary>
    [Parameter]
    public int ZIndex { get; set; } = 1000;

    [CascadingParameter(Name = "CascadingAnimation")]
    protected Animation? CascadingAnimation { get; set; }

    protected bool IsShown;
    protected string X { get; set; } = "";
    protected string Y { get; set; } = "";
    protected ContextMenuPosition MenuPosition { get; set; }
    protected string? TargetId { get; set; }
    protected ContextMenuTrigger? Trigger { get; set; }
    internal object? Data { get; set; }

    protected string ClassCalc {
        get {
            var template = Settings.GetTemplate(GetActiveTemplate());
            return CssClasses.Concat(template.MenuClass, Class);
        }
    }

    protected Animation GetActiveAnimation()
    {
        var animation = CascadingAnimation;
        if (Animation != null)
            animation = Animation;
        if (animation == null)
            animation = Settings.GetTemplate(GetActiveTemplate()).Animation;
        return animation.Value;
    }

    internal string GetActiveTemplate()
    {
        var templateName = CascadingTemplate;
        if (Template != null)
            templateName = Template;
        if (templateName == null)
            templateName = BlazorContextMenuSettings.DefaultTemplateName;
        return templateName;
    }

    protected string DisplayClassCalc {
        get {
            var template = Settings.GetTemplate(GetActiveTemplate());
            var (showingAnimationClass, hiddenAnimationClass) = GetAnimationClasses(GetActiveAnimation());
            return IsShown ?
                CssClasses.Concat(
                    showingAnimationClass,
                    template.MenuShownClass, ShownClass) :
                CssClasses.Concat(
                    hiddenAnimationClass,
                    template.MenuHiddenClass, HiddenClass);
        }
    }
    protected string ListClassCalc {
        get {
            var template = Settings.GetTemplate(GetActiveTemplate());
            return CssClasses.Concat(template.MenuListClass, ListClass);
        }
    }

    protected (string showingClass, string hiddenClass) GetAnimationClasses(Animation animation)
        => animation switch {
            BlazorContextMenu.Animation.None => ("", ""),
            BlazorContextMenu.Animation.FadeIn => ("blazor-context-menu__animations--fadeIn-shown",
                "blazor-context-menu__animations--fadeIn"),
            BlazorContextMenu.Animation.Grow => ("blazor-context-menu__animations--grow-shown",
                "blazor-context-menu__animations--grow"),
            BlazorContextMenu.Animation.Slide => ("blazor-context-menu__animations--slide-shown",
                "blazor-context-menu__animations--slide"),
            BlazorContextMenu.Animation.Zoom => ("blazor-context-menu__animations--zoom-shown",
                "blazor-context-menu__animations--zoom"),
            _ => throw new Exception("Animation not supported")
        };

    protected override void OnInitialized()
    {
        base.OnInitialized();
        if (string.IsNullOrEmpty(Id))
            throw new ArgumentNullException(nameof(Id));
        ContextMenuStorage.Register(this);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!ContextMenuHandler.ReferencePassedToJs) {
            await JS.InvokeAsync<object>($"{BlazorUICoreModule.ImportName}.blazorContextMenu.SetMenuHandlerReference", DotNetObjectReference.Create(ContextMenuHandler));
            ContextMenuHandler.ReferencePassedToJs = true;
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        ContextMenuStorage.Unregister(this);
    }

    internal async Task Show(string x, string y, string? targetId = null, ContextMenuTrigger? trigger = null)
    {
        if (trigger is null) {
            var rootMenu = MenuTreeTraverser.GetRootContextMenu(this);
            trigger = rootMenu?.GetTrigger();
        }

        if (trigger != null)
            Data = trigger.Data;

        if (OnAppearing.HasDelegate) {
            var eventArgs = new MenuAppearingEventArgs(Id, targetId, x, y, trigger, Data);
            await OnAppearing.InvokeAsync(eventArgs);
            x = eventArgs.X;
            y = eventArgs.Y;
            if (eventArgs.PreventShow)
                return;
        }

        IsShown = true;
        X = x;
        Y = y;
        MenuPosition = trigger?.MenuPosition ?? ContextMenuPosition.None;
        TargetId = targetId;
        Trigger = trigger;
        StateHasChanged();
    }

    internal async Task<bool> Hide()
    {
        if (OnHiding.HasDelegate) {
            var eventArgs = new MenuHidingEventArgs(Id, TargetId, X, Y, Trigger, Data);
            await OnHiding.InvokeAsync(eventArgs);
            if (eventArgs.PreventHide)
                return false;
        }

        IsShown = false;
        StateHasChanged();
        return true;
    }

    internal string? GetTarget()
        => TargetId;

    internal ContextMenuTrigger? GetTrigger()
        => Trigger;
}
