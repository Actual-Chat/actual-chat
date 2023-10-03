using Stl.Interception;

namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public partial class BackgroundUI(IServiceProvider services) : WorkerBase,
    IComputeService,
    INotifyInitialized,
    IBackgroundStateHandler
{
    private volatile int _isBackground;

    private readonly IMutableState<BackgroundState> _state = services
        .StateFactory()
        .NewMutable(
            BackgroundState.Foreground,
            StateCategories.Get(typeof(BackgroundUI), nameof(State)));

    private IBackgroundActivityProvider? _backgroundActivityProvider;

    private ILogger Log { get; } = services.LogFor(typeof(BackgroundUI));
    private IBackgroundActivityProvider BackgroundActivityProvider => _backgroundActivityProvider ??= services.GetRequiredService<IBackgroundActivityProvider>();
    private MomentClockSet Clocks { get; } = services.Clocks();

    public IState<BackgroundState> State => _state;

    [ComputeMethod]
    protected virtual Task<bool> GetIsBackground(CancellationToken cancellationToken)
        => Task.FromResult(_isBackground != 0);

    void INotifyInitialized.Initialized()
        => this.Start();

    void IBackgroundStateHandler.SetBackgroundState(bool isBackground)
    {
        Log.LogWarning("BackgroundUI-SET");
        var newIsBackground = isBackground ? 1 : 0;
        var oldIsBackground = Interlocked.Exchange(ref _isBackground, newIsBackground);
        if (newIsBackground == oldIsBackground)
            return;

        using (Computed.Invalidate())
            _ = GetIsBackground(default);
    }
}
