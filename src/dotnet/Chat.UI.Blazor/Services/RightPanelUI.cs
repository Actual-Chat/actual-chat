using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class RightPanelUI : IHasServices
{
    private readonly IStoredState<bool> _isVisible;
    private readonly object _lock = new();

    private History History { get; }
    private BrowserInfo BrowserInfo { get; }

    public IServiceProvider Services { get; }
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<bool> IsVisible => _isVisible;

    public RightPanelUI(IServiceProvider services)
    {
        Services = services;
        History = services.GetRequiredService<History>();
        BrowserInfo = services.GetRequiredService<BrowserInfo>();

        var stateFactory = services.StateFactory();
        var localSettings = services.GetRequiredService<LocalSettings>().WithPrefix(nameof(RightPanelUI));
        _isVisible = stateFactory.NewKvasStored<bool>(
            new (localSettings, nameof(IsVisible)) {
                InitialValue = false,
                Corrector = (isVisible, _) => new ValueTask<bool>(isVisible && !IsNarrow()),
                Category = StateCategories.Get(GetType(), nameof(IsVisible)),
            });
        History.Register(new OwnHistoryState(this, false));
        _isVisible.WhenRead.ContinueWith(
            _ => History.Dispatcher.InvokeAsync(() => SetIsVisible(_isVisible.Value)),
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

    private bool IsNarrow()
        => BrowserInfo.ScreenSize.Value.IsNarrow();

    // Nested types

    private sealed record OwnHistoryState(RightPanelUI Host, bool IsVisible) : HistoryState
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
