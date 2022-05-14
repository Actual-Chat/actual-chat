namespace ActualChat.UI.Blazor.Components;

public sealed class ContextMenuUI
{
    public IMutableState<RenderFragment?> Content { get; }

    public ContextMenuUI(IStateFactory stateFactory)
        => Content = stateFactory.NewMutable<RenderFragment?>();

    public void Open(RenderFragment menu)
        => Content.Value = menu;

    public void Close()
        => Content.Value = null;
}
