namespace ActualChat.UI.Blazor.Services;

public class RightPanel
{
    private readonly MutableState<bool> _isVisible;
    private readonly MutableState<bool> _isSearchMode;
    private readonly RightPanelStoredState _storedState;

    private UIHub Hub { get; }
    private History History => Hub.History;
    private Dispatcher Dispatcher => Hub.Dispatcher;
    private ILogger Log => field ??= Hub.LogFor(GetType());

    public PanelsUI Owner { get; }
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<bool> IsVisible => _isVisible;
    public IState<bool> IsSearchMode => _isSearchMode;

    public RightPanel(PanelsUI owner)
    {
        Owner = owner;
        Hub = owner.Hub;

        var stateFactory = Hub.StateFactory;
        _storedState = Hub.Services.GetRequiredService<RightPanelStoredState>();
        var isVisibleInitState = false;
        if (Owner.IsWide()) {
            if (_storedState.WhenRead.IsCompletedSuccessfully)
                isVisibleInitState = _storedState.IsVisible;
            else
                // False warning CS8604 detection
                Log!.LogWarning("Right panel stored state was not preloaded");
        }
        _isVisible = stateFactory.NewMutable(isVisibleInitState, StateCategories.Get(GetType(), nameof(IsVisible)));
        _isSearchMode = stateFactory.NewMutable(false, StateCategories.Get(GetType(), nameof(IsSearchMode)));
        var initialState = new OwnHistoryState(this, isVisibleInitState);
        History.Register(initialState);

        // Automatically open the right panel on a wide screen if it was open during the last session
        _ = Task.WhenAll(_storedState.WhenRead, Hub.History.WhenReady)
            .ContinueWith(_1 => {
                    if (Owner.IsWide() && _storedState.IsVisible)
                        SetIsVisible(true);
                },
                TaskScheduler.Default);
    }

    public void Toggle()
        => SetIsVisible(!IsVisible.Value);

    public void SearchToggle()
        => SetSearchMode(!IsSearchMode.Value);

    public void SetSearchMode(bool value)
    {
        if (_isSearchMode.Value == value)
            return;

        if (!_isVisible.Value && value)
            Toggle();
        _isSearchMode.Value = !IsSearchMode.Value;
    }

    public void SetIsVisible(bool value)
        => _ = Dispatcher.InvokeSafeAsync(() => {
            if (_isVisible.Value == value)
                return;

            _isVisible.Value = value;
            if (Owner.IsWide())
                _storedState.IsVisible = value;
            if (!value)
                _isSearchMode.Value = false;
            History.Save<OwnHistoryState>();
        }, Log);

    // Nested types

    private sealed record OwnHistoryState(RightPanel Host, bool IsVisible) : HistoryState
    {
        public override int BackStepCount => IsVisible ? 1 : 0;

        public override string Format()
            => IsVisible.ToString();

        public override HistoryState Save()
            => With(Host.IsVisible.Value);

        public override void Apply(HistoryTransition transition)
            => Host.SetIsVisible(IsVisible);

        public override HistoryState? Back()
            => BackStepCount == 0 ? null : With(!IsVisible);

        // "With" helpers

        public OwnHistoryState With(bool isVisible)
            => IsVisible == isVisible ? this : this with { IsVisible = isVisible };
    }
}
