namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class MiddlePanel(UIHub hub) : UIServiceBase<UIHub>(hub), IComputeService
{
    private static readonly TimeSpan ContentSwapCatchPeriod = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ContentSwapDisplayTimeout = TimeSpan.FromSeconds(0.5);
    private Task? _contentSwapTask;
    private bool _canRenderValue;
    // 1 while a settled pull is waiting to be reported: that change is already on screen, so the
    // visibility change it triggers must not wait for a transition that has already run.
    private int _isNextVisibilityChangeSettled;
    // CpuTimestamp.Value: the earliest moment IsVisible may report false. Written on the dispatcher
    // as a side panel's visibility flips, read from the compute.
    private long _canHideAtValue;

    public PanelsUI Owner => field ??= Hub.PanelsUI;
    // Latched, so a render block gating on CanRender doesn't have to await it
    public bool CanRenderValue => Volatile.Read(ref _canRenderValue);

    [ComputeMethod(ConsolidationDelay = 0)]
    public virtual async Task<bool> IsVisible(CancellationToken cancellationToken)
    {
        // Stays visible while a side panel covering it slides in. Consolidated because that is two
        // invalidations - the cover, then the deadline - and only the second changes the answer.
        var screenSize = await Owner.ScreenSize.Use(cancellationToken).ConfigureAwait(false);
        if (screenSize.IsWide())
            return true;

        var isCovered = await Owner.Left.IsVisible.Use(cancellationToken).ConfigureAwait(false)
            || await Owner.Right.IsVisible.Use(cancellationToken).ConfigureAwait(false);
        if (!isCovered)
            return true;

        // Nothing invalidates when a deadline merely elapses, so this comes back on its own rather
        // than holding the compute - a sleeping one would stall every other invalidation behind it.
        var hideIn = new CpuTimestamp(Volatile.Read(ref _canHideAtValue)) - CpuTimestamp.Now;
        if (hideIn <= TimeSpan.Zero)
            return false;

        Computed.GetCurrent().Invalidate(hideIn);
        return true;
    }

    [ComputeMethod]
    public virtual async Task<bool> CanRender(CancellationToken cancellationToken)
    {
        // Gates this panel's first render only, and one-way: a panel covering it later doesn't
        // un-render it.
        if (Volatile.Read(ref _canRenderValue))
            return true;

        // Nothing covers this panel, or the left panel has had its turn
        var canRender = await IsVisible(cancellationToken).ConfigureAwait(false)
            || await Owner.Left.IsSettled.Use(cancellationToken).ConfigureAwait(false);
        if (canRender)
            Volatile.Write(ref _canRenderValue, true);

        return canRender;
    }

    // ContentSwap related

    public void NotifyContentSwapStarted(Task whenDisplayed)
        => Volatile.Write(ref _contentSwapTask, whenDisplayed);

    public async Task WhenContentSwapped()
    {
        // The content replacing the side panels registers its swap while the navigation settles, so
        // that's when we look for one; nothing registered by then means nothing is coming
        await Task.Delay(ContentSwapCatchPeriod).ConfigureAwait(false);
        if (Volatile.Read(ref _contentSwapTask) is { IsCompleted: false } contentSwapTask)
            await contentSwapTask.WaitAsync(ContentSwapDisplayTimeout).SilentAwait(false);
    }

    // Protected/internal methods

    internal void NotifyVisibilityChanging(bool isVisible)
    {
        // Called before the invalidation that reaches IsVisible, so the deadline is in place by the
        // time it recomputes. Only covering waits - uncovering reveals content immediately.
        var isSettled = Interlocked.Exchange(ref _isNextVisibilityChangeSettled, 0) != 0;
        var canHideAt = CpuTimestamp.Now;
        if (isVisible && !isSettled)
            canHideAt += PanelsUI.PanelTransitionDuration;
        Volatile.Write(ref _canHideAtValue, canHideAt.Value);
    }

    internal void NotifyPullSettled()
        // SideNav calls this once a pull settles: the panel is already where it belongs, so the
        // visibility change that follows must not wait for a transition that has already run.
        => Interlocked.Exchange(ref _isNextVisibilityChangeSettled, 1);
}
