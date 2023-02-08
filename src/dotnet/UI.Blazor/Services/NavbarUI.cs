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
        => HistoryUI.Update<OwnHistoryState>((_, state) => state.With(value));

    // Private methods

    private bool IsNarrow()
        => BrowserInfo.ScreenSize.Value.IsNarrow();

    // Nested types

    private sealed record OwnHistoryState(NavbarUI Host, bool IsVisible) : HistoryState
    {
        public override int BackCount => IsVisible ? 0 : 1;

        public override string ToString()
            => $"{nameof(NavbarUI)}.{GetType().Name}({IsVisible})";

        public override HistoryState Fix(HistoryChange change)
        {
            if (!Host.IsNarrow())
                return With(true);

            var localUrl = change.Item.LocalUrl;
            var isAtRoot = localUrl.IsChatRoot() || localUrl.IsDocsRoot();
            return isAtRoot ? With(true) : this;
        }

        public override HistoryState Apply(HistoryChange change)
        {
            bool isChanged;
            lock (Host._lock) {
                isChanged = Host._isVisible.Value != IsVisible;
                Host._isVisible.Value = IsVisible;
            }
            if (isChanged) {
                Host.Log.LogDebug("OwnHistoryState.Apply: {State}", this);
                Host.VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
            return this;
        }

        // "With" helpers

        public OwnHistoryState With(bool isVisible)
            => IsVisible == isVisible ? this : this with { IsVisible = isVisible };
    }
}
