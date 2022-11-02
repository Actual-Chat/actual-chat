namespace ActualChat.Chat.UI.Blazor.Services;

public class PlusButtonPanelUI
{
    private readonly IMutableState<bool> _isVisible;

    public IState<bool> IsVisible
        => _isVisible;

    public PlusButtonPanelUI(IServiceProvider services)
    {
        var stateFactory = services.StateFactory();
        _isVisible = stateFactory.NewMutable<bool>();
    }

    public void Switch()
        => ChangeVisibility(!IsVisible.Value);

    public void ChangeVisibility(bool visible)
    {
        if (_isVisible.Value == visible)
            return;
        ChangeVisibilityInternal(visible);
    }

    private void ChangeVisibilityInternal(bool visible)
        => _isVisible.Value = visible;
}
