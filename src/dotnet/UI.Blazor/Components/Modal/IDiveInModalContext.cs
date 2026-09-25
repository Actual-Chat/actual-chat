namespace ActualChat.UI.Blazor.Components;

public interface IDiveInModalContext
{
    public MutablePropertyBag Items { get; }
    public bool IsInnerStep { get; }

    bool IsTopmostPage(Type pageComponentType);
    void StepIn(DiveInDialogPage pageDescriptor);
    void Close();
    void StateHasChanged();
}
