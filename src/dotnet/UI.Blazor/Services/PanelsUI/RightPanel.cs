using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

public class RightPanel
{
    private const string StatePrefix = nameof(RightPanel) + "UI";
    private readonly IStoredState<bool> _isVisible;
    private readonly object _lock = new();

    private IServiceProvider Services => Owner.Services;
    private History History => Owner.History;

    public PanelsUI Owner { get; }
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<bool> IsVisible => _isVisible;

    public RightPanel(PanelsUI owner)
    {
        Owner = owner;
        var stateFactory = Services.StateFactory();
        var localSettings = Services.GetRequiredService<LocalSettings>().WithPrefix(StatePrefix);
        _isVisible = stateFactory.NewKvasStored<bool>(
            new (localSettings, nameof(IsVisible)) {
                InitialValue = false,
                Corrector = (isVisible, _) => new ValueTask<bool>(isVisible && !Owner.IsNarrow()),
                Category = StateCategories.Get(GetType(), nameof(IsVisible)),
            });
        _isVisible.WhenRead.ContinueWith(
            _ => {
                var isVisible = _isVisible.Value;
                History.Register(new OwnHistoryState(this, isVisible));
                return History.Dispatcher.InvokeAsync(() => SetIsVisible(isVisible));
            },
            TaskScheduler.Default);
    }

    public void Toggle()
        => SetIsVisible(!IsVisible.Value);

    public void SetIsVisible(bool value)
    {
        bool oldIsVisible;
        lock (_lock) {
            oldIsVisible = _isVisible.Value;
            if (oldIsVisible != value)
                _isVisible.Value = value;
        }
        if (oldIsVisible != value)
            History.Save<OwnHistoryState>();
    }

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
