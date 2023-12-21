using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class RightPanel
{
    private const string StatePrefix = nameof(RightPanel) + "UI";
    private readonly IMutableState<bool> _isVisible;
    private readonly IStoredState<bool> _isVisibleStored;

    private UIHub Hub { get; }
    private History History => Hub.History;
    private Dispatcher Dispatcher => Hub.Dispatcher;

    public PanelsUI Owner { get; }
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<bool> IsVisible => _isVisible;

    public RightPanel(PanelsUI owner)
    {
        Owner = owner;
        Hub = owner.Hub;

        var stateFactory = Hub.StateFactory();
        _isVisible = stateFactory.NewMutable(false, StateCategories.Get(GetType(), nameof(IsVisible)));
        var initialState = new OwnHistoryState(this, false);
        History.Register(initialState);

        var localSettings = Hub.LocalSettings().WithPrefix(StatePrefix);
        _isVisibleStored = stateFactory.NewKvasStored<bool>(
            new (localSettings, nameof(IsVisible)) {
                InitialValue = false,
                Category = StateCategories.Get(GetType(), nameof(IsVisible) + "Stored"),
            });

        // Automatically open right panel on wide screen if it was open during last session.
        _ = Task.WhenAll(_isVisibleStored.WhenRead, Hub.History.WhenReady)
            .ContinueWith(_1 => {
                    if (Owner.IsWide() && _isVisibleStored.Value)
                        SetIsVisible(true);
                },
                TaskScheduler.Default);
    }

    public void Toggle()
        => SetIsVisible(!IsVisible.Value);

    public void SetIsVisible(bool value)
        => _ = Dispatcher.InvokeSafeAsync(() => {
            if (_isVisible.Value == value)
                return;

            _isVisible.Value = value;
            if (Owner.IsWide())
                _isVisibleStored.Value = value;
            History.Save<OwnHistoryState>();
        }, DefaultLog);

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
