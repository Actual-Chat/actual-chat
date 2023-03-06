namespace ActualChat.UI.Blazor.Services;

public class MiddlePanel
{
    private readonly IComputedState<bool> _isVisible;

    public PanelsUI Owner { get; }
    public IState<bool> IsVisible => _isVisible;

    public MiddlePanel(PanelsUI owner)
    {
        Owner = owner;
        _isVisible = Owner.Services.StateFactory().NewComputed(
            new ComputedState<bool>.Options {
                UpdateDelayer = FixedDelayer.ZeroUnsafe,
                InitialValue = ComputeInitialIsVisible(),
                Category = StateCategories.Get(GetType(), nameof(IsVisible)),
            },
            ComputeIsVisible);
    }

    private async Task<bool> ComputeIsVisible(IComputedState<bool> state, CancellationToken cancellationToken)
    {
        var screenSize = await Owner.ScreenSize.Use(cancellationToken);
        var isWide = screenSize.IsWide();
        if (isWide)
            return true;
        if (await Owner.Left.IsVisible.Use(cancellationToken))
            return false;
        if (await Owner.Right.IsVisible.Use(cancellationToken))
            return false;
        return true;
    }

    private bool ComputeInitialIsVisible()
        => Owner.IsWide() || !(Owner.Left.IsVisible.Value || Owner.Right.IsVisible.Value);
}
