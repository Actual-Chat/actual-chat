namespace BlazorContextMenu;

public abstract class MenuTreeComponent : ComponentBase, IDisposable
{
    private readonly List<MenuTreeComponent> _childComponents = new ();

    [CascadingParameter(Name = "ParentComponent")]
    public MenuTreeComponent? ParentComponent { get; protected set; }

    public IReadOnlyList<MenuTreeComponent> GetChildComponents()
        => _childComponents.AsReadOnly();

    protected void RegisterChild(MenuTreeComponent childComponent)
        => _childComponents.Add(childComponent);

    protected void RemoveChild(MenuTreeComponent childComponent)
        => _childComponents.Remove(childComponent);

    protected override void OnInitialized()
    {
        if (ParentComponent == null)
            return;
        ParentComponent.RegisterChild(this);
        ParentComponent.StateHasChanged();
    }

    public virtual void Dispose()
        => ParentComponent?.RemoveChild(this);
}
