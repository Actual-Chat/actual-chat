namespace ActualChat.UI.Blazor.Services;

public class LeftPanel
{
    private readonly IMutableState<bool> _isVisible;
    private ILogger? _log;

    private IServiceProvider Services => Owner.Services;
    private ILogger Log => _log ??= Services.LogFor(GetType());
    private History History => Owner.History;
    private Dispatcher Dispatcher => Owner.Dispatcher;

    public PanelsUI Owner { get; }
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<bool> IsVisible => _isVisible;
    public event Action? VisibilityChanged;

    public LeftPanel(PanelsUI owner, bool? initialLeftPanelIsVisible)
    {
        Owner = owner;
        var initialIsVisible = true;
        var isVisibleOverride = GetIsVisibleOverride()
            ?? initialLeftPanelIsVisible
            ?? (History.LocalUrl.IsChat() ? false : null);
        if (isVisibleOverride == false)
            initialIsVisible = false;
        _isVisible = Services.StateFactory().NewMutable(initialIsVisible);
        History.Register(new OwnHistoryState(this, initialIsVisible));

        // Log.LogDebug($".ctor: {History.LocalUrl} -> {isVisibleOverride}, {initialLeftPanelIsVisible}");
    }

    public void SetIsVisible(bool value)
        => _ = Dispatcher.InvokeAsync(() => {
            if (GetIsVisibleOverride() is { } valueOverride)
                value = valueOverride;
            if (_isVisible.Value == value)
                return;

            // Log.LogDebug("SetIsVisible: {IsVisible}", value);
            _isVisible.Value = value;
            History.Save<OwnHistoryState>();
            VisibilityChanged?.Invoke();
        });

    // Private methods

    private bool? GetIsVisibleOverride()
    {
        if (Owner.IsWide())
            return true;

        var localUrl = History.LocalUrl;
        if (localUrl.IsDocsOrDocsRoot())
            return false; // This panel isn't used in narrow mode in /docs
        if (localUrl.IsChatRoot())
            return true;

        return null;
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
