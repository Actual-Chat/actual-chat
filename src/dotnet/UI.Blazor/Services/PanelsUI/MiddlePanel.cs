namespace ActualChat.UI.Blazor.Services;

public sealed class MiddlePanel : IDisposable
{
    private static readonly TimeSpan ContentSwapCatchPeriod = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ContentSwapDisplayTimeout = TimeSpan.FromSeconds(0.5);

    private readonly ComputedState<bool> _isVisible;
    private readonly ComputedState<bool> _canRender;
    private Task? _contentSwapTask;
    private bool _canRenderValue;

    public PanelsUI Owner { get; }
    public IState<bool> IsVisible => _isVisible;
    public IState<bool> CanRender => _canRender;

    public MiddlePanel(PanelsUI owner)
    {
        Owner = owner;
        var stateFactory = Owner.Hub.StateFactory;
        var initialIsVisible = Owner.IsWide() || !(Owner.Left.IsVisible.Value || Owner.Right.IsVisible.Value);
        _isVisible = stateFactory.NewComputed(
            new ComputedState<bool>.Options {
                UpdateDelayer = FixedDelayer.NoneUnsafe,
                InitialValue = initialIsVisible,
                Category = StateCategories.Get(GetType(), nameof(IsVisible)),
            },
            ComputeIsVisible);
        _canRender = stateFactory.NewComputed(
            new ComputedState<bool>.Options {
                UpdateDelayer = FixedDelayer.NoneUnsafe,
                InitialValue = initialIsVisible,
                Category = StateCategories.Get(GetType(), nameof(CanRender)),
            },
            ComputeCanRender);
    }

    public void Dispose()
    {
        _isVisible.Dispose();
        _canRender.Dispose();
    }

    public void NotifyContentSwapStarted(Task whenDisplayed)
        => Volatile.Write(ref _contentSwapTask, whenDisplayed);

    public async Task WhenContentDisplayed()
    {
        // The content replacing the side panels registers its swap while the navigation settles, so
        // that's when we look for one; nothing registered by then means nothing is coming
        await Task.Delay(ContentSwapCatchPeriod).ConfigureAwait(false);
        if (Volatile.Read(ref _contentSwapTask) is { IsCompleted: false } contentSwapTask)
            await contentSwapTask.WaitAsync(ContentSwapDisplayTimeout).SilentAwait(false);
    }

    // Private methods

    private async Task<bool> ComputeIsVisible(CancellationToken cancellationToken)
    {
        var screenSize = await Owner.ScreenSize.Use(cancellationToken).ConfigureAwait(false);
        if (screenSize.IsWide())
            return true;

        if (await Owner.Left.IsVisible.Use(cancellationToken).ConfigureAwait(false))
            return false;
        if (await Owner.Right.IsVisible.Use(cancellationToken).ConfigureAwait(false))
            return false;

        return true;
    }

    private async Task<bool> ComputeCanRender(CancellationToken cancellationToken)
    {
        // Gates this panel's first render only, and one-way: a panel covering it later doesn't
        // un-render it. Latched in a field - _canRender runs this before it's assigned
        if (Volatile.Read(ref _canRenderValue))
            return true;

        // Nothing covers this panel, or the left panel has had its turn
        var canRender = await IsVisible.Use(cancellationToken).ConfigureAwait(false)
            || await Owner.Left.IsSettled.Use(cancellationToken).ConfigureAwait(false);
        if (canRender)
            Volatile.Write(ref _canRenderValue, true);

        return canRender;
    }
}
