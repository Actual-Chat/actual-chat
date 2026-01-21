namespace ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

public class ComputedStateViewState<T>(
    ComputedState<T>.Options options,
    ComputedStateView<T> view,
    IServiceProvider services) : ComputedState<T>(options, services, false), IHasInitialize
{
    protected ComputedStateView<T> View { get; } = view;

    public static ComputedStateViewState<T> New(
        Options settings,
        ComputedStateView<T> view,
        IServiceProvider services)
        => new (settings, view, services);

    protected override Task Compute(CancellationToken cancellationToken)
        => GetComputeTaskIfDisposed() ?? View.StateComputer(cancellationToken);

    void IHasInitialize.Initialize(object? settings)
        => base.Initialize((Options)settings!);
}
