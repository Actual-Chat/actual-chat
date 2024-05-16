namespace ActualChat.UI.Blazor.Services;

public class MiddlePanel : IDisposable
{
    private readonly ComputedState<bool> _isVisible;

    public PanelsUI Owner { get; }
    public IState<bool> IsVisible => _isVisible;

    public MiddlePanel(PanelsUI owner)
    {
        Owner = owner;
        _isVisible = Owner.Hub.StateFactory().NewComputed(
            new ComputedState<bool>.Options {
                UpdateDelayer = FixedDelayer.NoneUnsafe,
                InitialValue = ComputeInitialIsVisible(),
                Category = StateCategories.Get(GetType(), nameof(IsVisible)),
            },
            ComputeIsVisible);
    }

    public void Dispose()
        => _isVisible.Dispose();

    private async Task<bool> ComputeIsVisible(CancellationToken cancellationToken)
    {
        var screenSize = await Owner.ScreenSize.Use(cancellationToken).ConfigureAwait(false);
        var isWide = screenSize.IsWide();
        if (isWide)
            return true;

        if (await Owner.Left.IsVisible.Use(cancellationToken).ConfigureAwait(false))
            return false;
        if (await Owner.Right.IsVisible.Use(cancellationToken).ConfigureAwait(false))
            return false;

        return true;
    }

    private bool ComputeInitialIsVisible()
        => Owner.IsWide() || !(Owner.Left.IsVisible.Value || Owner.Right.IsVisible.Value);
}
