using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class RightPanelUI : IHasServices
{
    private readonly IStoredState<bool> _isVisible;
    private readonly object _lock = new();

    private HistoryUI HistoryUI { get; }
    private BrowserInfo BrowserInfo { get; }

    public IServiceProvider Services { get; }
    public IState<bool> IsVisible => _isVisible;

    public RightPanelUI(IServiceProvider services)
    {
        Services = services;
        HistoryUI = services.GetRequiredService<HistoryUI>();
        BrowserInfo = services.GetRequiredService<BrowserInfo>();

        var stateFactory = services.StateFactory();
        var localSettings = services.GetRequiredService<LocalSettings>().WithPrefix(nameof(RightPanelUI));
        _isVisible = stateFactory.NewKvasStored<bool>(
            new (localSettings, nameof(IsVisible)) {
                InitialValue = false,
                Corrector = (isVisible, _) => new ValueTask<bool>(isVisible && !IsNarrow()),
                Category = StateCategories.Get(GetType(), nameof(IsVisible)),
            });
        HistoryUI.Register(new OwnHistoryState(this, false));
        _isVisible.WhenRead.ContinueWith(
            _ => HistoryUI.Hub.Dispatcher.InvokeAsync(() => SetIsVisible(_isVisible.Value)),
            TaskScheduler.Default);
    }

    public void Toggle()
        => SetIsVisible(!IsVisible.Value);

    public void SetIsVisible(bool value)
        => HistoryUI.Update<OwnHistoryState>((_, state) => state.With(value));

    private bool IsNarrow()
        => BrowserInfo.ScreenSize.Value.IsNarrow();

    // Nested types

    private sealed record OwnHistoryState(RightPanelUI Host, bool IsVisible) : HistoryState
    {
        public override int BackCount => IsVisible ? 1 : 0;

        public override string ToString()
            => $"{nameof(RightPanelUI)}.{GetType().Name}({IsVisible})";

        public override HistoryState Apply(HistoryChange change)
        {
            lock (Host._lock)
                Host._isVisible.Value = IsVisible;
            return this;
        }

        // "With" helpers

        public OwnHistoryState With(bool isVisible)
            => IsVisible == isVisible ? this : this with { IsVisible = isVisible };
    }
}
