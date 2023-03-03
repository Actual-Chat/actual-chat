using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public class NavbarUI : WorkerBase
{
    private readonly IMutableState<bool> _isVisible;
    private readonly object _lock = new();

    private ILogger Log { get; }
    private History History { get; }
    private IState<ScreenSize> ScreenSize { get; }

    // ReSharper disable once InconsistentlySynchronizedField
    public IState<bool> IsVisible => _isVisible;
    public string SelectedGroupId { get; private set; } = "chats";
    public string SelectedGroupTitle { get; private set; } = "Chats";
    public event EventHandler? SelectedGroupChanged;
    public event EventHandler? VisibilityChanged;

    public NavbarUI(IServiceProvider services)
    {
        Log = services.LogFor(GetType());
        History = services.GetRequiredService<History>();
        ScreenSize = services.GetRequiredService<BrowserInfo>().ScreenSize;

        _isVisible = services.StateFactory().NewMutable(true);
        History.Register(new OwnHistoryState(this, true));
        Start();
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
        var localUrl = History.LocalUrl;
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
            History.Save<OwnHistoryState>();
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Private & protected methods

    protected override Task RunInternal(CancellationToken cancellationToken)
        => History.Dispatcher.InvokeAsync(async () => {
            var lastIsWide = IsWide();
            await foreach (var _ in ScreenSize.Changes(cancellationToken)) {
                var isWide = IsWide();
                if (lastIsWide != isWide) {
                    lastIsWide = isWide;
                    SetIsVisible(IsVisible.Value); // It changes to the right one anyway
                }
            }
        });

    private bool IsWide()
        => ScreenSize.Value.IsWide();

    // Nested types

    private sealed record OwnHistoryState(NavbarUI Host, bool IsVisible) : HistoryState
    {
        public override int BackStepCount => IsVisible ? 0 : 1;
        public override bool IsUriDependent => true;

        public override string Format()
            => IsVisible.ToString();

        public override HistoryState Save()
            => With(Host.IsVisible.Value);

        public override void Apply(HistoryTransition transition)
        {
            var isHistoryMove = transition.LocationChangeKind is LocationChangeKind.HistoryMove;
            Host.SetIsVisible(IsVisible && isHistoryMove);
        }

        public override HistoryState? Back()
            => BackStepCount == 0 ? null : With(!IsVisible);

        // "With" helpers

        public OwnHistoryState With(bool isVisible)
            => IsVisible == isVisible ? this : this with { IsVisible = isVisible };
    }
}
