namespace ActualChat.UI.Blazor.Services;

public sealed class LeftPanel : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(333);

    private readonly MutableState<bool> _isVisible;
    private readonly ComputedState<bool> _canBeHidden;
    private readonly MutableState<bool> _isSettled;
    private int _isRendered;

    private UIHub Hub { get; }
    private History History => Hub.History;
    private Dispatcher Dispatcher => Hub.Dispatcher;
    private ILogger Log => field ??= Hub.LogFor(GetType());

    public PanelsUI Owner { get; }
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<bool> IsVisible => _isVisible;
    public IState<bool> CanBeHidden => _canBeHidden;
    public IState<bool> IsSettled => _isSettled;
    public event Action? VisibilityChanged;

    public LeftPanel(PanelsUI owner)
    {
        Owner = owner;
        Hub = owner.Hub;

        var stateFactory = Hub.StateFactory;
        _isVisible = stateFactory.NewMutable(true, StateCategories.Get(GetType(), nameof(IsVisible)));
        _isSettled = stateFactory.NewMutable(false, StateCategories.Get(GetType(), nameof(IsSettled)));
        _canBeHidden = stateFactory.NewComputed(
            new ComputedState<bool>.Options() {
                UpdateDelayer = FixedDelayer.NextTick,
                Category = StateCategories.Get(GetType(), nameof(CanBeHidden)),
            },
            ComputeCanBeHidden);
        History.Register(new OwnHistoryState(this, true));
        // Log.LogInformation("InitialIsVisible: {InitialIsVisible} @ {Url}", initialIsVisible, History.LocalUrl);
    }

    public void Dispose()
        => _canBeHidden.Dispose();

    public void MarkRendered()
    {
        // IsSettled is what the middle panel waits for before its own first render, so the two don't
        // compete for the same frame at startup
        if (Interlocked.Exchange(ref _isRendered, 1) != 0)
            return;

        _ = MarkSettledAfterDelay();
        return;

        async Task MarkSettledAfterDelay() {
            await Task.Delay(SettleDelay).ConfigureAwait(true);
            _isSettled.Value = true;
        }
    }

    public void SetIsVisible(bool value)
        => _ = Dispatcher.InvokeSafeAsync(() => {
            if (GetIsVisibleOverride() is { } valueOverride)
                value = valueOverride;
            if (_isVisible.Value == value)
                return;

            // Log.LogDebug("SetIsVisible: {IsVisible}", value);
            _isVisible.Value = value;
            History.Save<OwnHistoryState>();
            VisibilityChanged?.Invoke();
        }, Log);

    // Private methods

    private bool? GetIsVisibleOverride()
    {
        if (Owner.IsWide())
            return true;

        var localUrl = History.LocalUrl;
        if (localUrl.IsDocsOrDocsRoot())
            return false; // This panel isn't used in narrow mode in /docs

        return null;
    }

    private async Task<bool> ComputeCanBeHidden(CancellationToken cancellationToken)
    {
        var screenSize = await Owner.ScreenSize.Use(cancellationToken).ConfigureAwait(false);
        if (screenSize.IsWide())
            return false;

        var currentItem = await History.State.Use(cancellationToken).ConfigureAwait(false);
        var localUrl = new LocalUrl(currentItem.Url);
        if (localUrl.IsDocsOrDocsRoot())
            return true; // This panel isn't used in narrow mode in /docs

        return !localUrl.IsChatRoot();
    }

    // Nested types

    private sealed record OwnHistoryState(LeftPanel Host, bool IsVisible) : HistoryState
    {
        public override int BackStepCount => IsVisible ? 0 : 1;
        public override bool IsUriDependent => true;

        public override string Format()
            => IsVisible.ToString();

        public override HistoryState Save()
            => With(Host.IsVisible.Value);

        public override void Apply(HistoryTransition transition)
        {
            Host.SetIsVisible(IsVisible);
            _ = Host.Owner.HandleHistoryTransition(transition);
        }

        public override HistoryState? Back()
            => BackStepCount == 0 ? null : With(!IsVisible);

        // "With" helpers

        public OwnHistoryState With(bool isVisible)
            => IsVisible == isVisible ? this : this with { IsVisible = isVisible };
    }
}
