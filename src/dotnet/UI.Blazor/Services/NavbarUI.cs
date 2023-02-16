namespace ActualChat.UI.Blazor.Services;

public class NavbarUI
{
    private readonly IMutableState<bool> _isVisible;
    private readonly object _lock = new();

    private ILogger Log { get; }
    private HistoryUI HistoryUI { get; }
    private NavigationManager Nav { get; }
    private BrowserInfo BrowserInfo { get; }

    // ReSharper disable once InconsistentlySynchronizedField
    public IState<bool> IsVisible => _isVisible;
    public string SelectedGroupId { get; private set; } = "chats";
    public string SelectedGroupTitle { get; private set; } = "Chats";
    public event EventHandler? SelectedGroupChanged;
    public event EventHandler? VisibilityChanged;

    public NavbarUI(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        HistoryUI = services.GetRequiredService<HistoryUI>();
        Nav = services.GetRequiredService<NavigationManager>();
        BrowserInfo = services.GetRequiredService<BrowserInfo>();

        _isVisible = services.StateFactory().NewMutable(true);
        HistoryUI.Register(new OwnHistoryState(this, true));
    }

    // NOTE(AY): Any public member of this type can be used only from Blazor Dispatcher's thread

    public void SelectGroup(string id, string title)
    {
        if (OrdinalEquals(id, SelectedGroupId))
            return;

        SelectedGroupId = id;
        SelectedGroupTitle = title;
        SelectedGroupChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetIsVisible(bool value)
    {
        var localUrl = HistoryUI.LocalUrl;
        value |= localUrl.IsChatRoot(); // Always visible if @ /chat
        value &= !localUrl.IsDocsOrDocsRoot(); // Always invisible if @ /docs*
        value |= IsWide(); // Always visible if wide

        bool oldIsVisible;
        lock (_lock) {
            oldIsVisible = _isVisible.Value;
            if (oldIsVisible != value)
                _isVisible.Value = value;
        }
        if (oldIsVisible != value) {
            Log.LogDebug("Visibility changed: {IsVisible}", value);
            HistoryUI.Save<OwnHistoryState>();
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Private methods

    private bool IsWide()
        => BrowserInfo.ScreenSize.Value.IsWide();

    // Nested types

    private sealed record OwnHistoryState(NavbarUI Host, bool IsVisible) : HistoryState
    {
        public override int BackStepCount => IsVisible ? 0 : 1;
        public override bool MustApplyUnconditionally => true;

        public override string ToString()
            => $"{nameof(NavbarUI)}.{GetType().Name}({IsVisible})";

        public override HistoryState Save()
            => With(Host.IsVisible.Value);

        public override void Apply(HistoryTransition transition)
            => Host.SetIsVisible(IsVisible && !transition.IsUriChanged);

        public override HistoryState? Back()
            => BackStepCount == 0 ? null : With(!IsVisible);

        // "With" helpers

        public OwnHistoryState With(bool isVisible)
            => IsVisible == isVisible ? this : this with { IsVisible = isVisible };
    }
}
