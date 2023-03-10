namespace ActualChat.UI.Blazor.Components;

public class RegionVisibility : IDisposable
{
    private readonly IState<bool> _isViewportHidden;
    private readonly IMutableState<bool> _isOverallVisible;

    public IState<bool> IsPanelVisible { get; }
    public IState<bool> IsOverallVisible => _isOverallVisible;

    public RegionVisibility(IStateFactory stateFactory, IState<bool> isViewportHidden, IState<bool> isPanelVisible)
    {
        if (stateFactory == null) throw new ArgumentNullException(nameof(stateFactory));

        _isViewportHidden = isViewportHidden ?? throw new ArgumentNullException(nameof(isViewportHidden));
        IsPanelVisible = isPanelVisible ?? throw new ArgumentNullException(nameof(isPanelVisible));
        _isOverallVisible = stateFactory.NewMutable<bool>();
        isPanelVisible.Updated += IsPanelVisibleOnUpdated;
        _isViewportHidden.Updated += IsViewportHiddenOnUpdated;
        UpdateIsOverallVisible();
    }

    private void IsViewportHiddenOnUpdated(IState<bool> arg1, StateEventKind arg2)
        => UpdateIsOverallVisible();

    private void IsPanelVisibleOnUpdated(IState<bool> arg1, StateEventKind arg2)
        => UpdateIsOverallVisible();

    private void UpdateIsOverallVisible()
        => _isOverallVisible.Value = !_isViewportHidden.Value && IsPanelVisible.Value;

    public void Dispose()
    {
        IsPanelVisible.Updated -= IsPanelVisibleOnUpdated;
        _isViewportHidden.Updated -= IsViewportHiddenOnUpdated;
    }
}
