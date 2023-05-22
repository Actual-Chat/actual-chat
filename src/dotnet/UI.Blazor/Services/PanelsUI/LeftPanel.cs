namespace ActualChat.UI.Blazor.Services;

public class LeftPanel
{
    private readonly IMutableState<bool> _isVisible;
    private readonly object _lock = new();

    private IServiceProvider Services => Owner.Services;
    private History History => Owner.History;

    public PanelsUI Owner { get; }
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<bool> IsVisible => _isVisible;
    public event EventHandler? VisibilityChanged;

    public LeftPanel(PanelsUI owner, bool? initialLeftPanelIsVisible)
    {
        Owner = owner;
        var isVisibleOverride = GetIsVisibleOverride()
            ?? initialLeftPanelIsVisible
            ?? (History.LocalUrl.IsChat() ? false : null);

        var isVisible = isVisibleOverride ?? true;
        _isVisible = Services.StateFactory().NewMutable(isVisible);
        History.Register(new OwnHistoryState(this, isVisible));

        if (isVisibleOverride == false)
            SetIsVisible(false);
    }

    public void SetIsVisible(bool value)
    {
        if (GetIsVisibleOverride() is { } valueOverride)
            value = valueOverride;

        bool oldIsVisible;
        lock (_lock) {
            oldIsVisible = _isVisible.Value;
            if (oldIsVisible != value)
                _isVisible.Value = value;
        }
        if (oldIsVisible != value) {
            History.Save<OwnHistoryState>();
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Private methods

    private bool? GetIsVisibleOverride()
    {
        if (IsWide())
            return true;

        var localUrl = History.LocalUrl;
        if (localUrl.IsDocsOrDocsRoot())
            return false; // This panel isn't used in narrow mode in /docs
        if (localUrl.IsChatRoot())
            return true;

        return null;
    }

    private bool IsWide()
        => Owner.ScreenSize.Value.IsWide();

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
            => Host.SetIsVisible(IsVisible);

        public override HistoryState? Back()
            => BackStepCount == 0 ? null : With(!IsVisible);

        // "With" helpers

        public OwnHistoryState With(bool isVisible)
            => IsVisible == isVisible ? this : this with { IsVisible = isVisible };
    }
}
