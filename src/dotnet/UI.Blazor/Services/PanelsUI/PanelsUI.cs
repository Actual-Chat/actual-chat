using Stl.Interception;

namespace ActualChat.UI.Blazor.Services;

public class PanelsUI : WorkerBase, IHasServices
{
    public IServiceProvider Services { get; }
    public History History { get; }
    public IState<ScreenSize> ScreenSize { get; }

    public LeftPanel Left { get; }
    public MiddlePanel Middle { get; }
    public RightPanel Right { get; }

    public PanelsUI(IServiceProvider services)
    {
        Services = services;
        History = services.GetRequiredService<History>();
        ScreenSize = services.GetRequiredService<BrowserInfo>().ScreenSize;
        Left = new LeftPanel(this);
        Right = new RightPanel(this);
        Middle = new MiddlePanel(this);
        Start();
    }

    public bool IsNarrow()
        => ScreenSize.Value.IsNarrow();
    public bool IsWide()
        => ScreenSize.Value.IsWide();

    protected override Task RunInternal(CancellationToken cancellationToken)
        => History.Dispatcher.InvokeAsync(async () => {
            var lastIsWide = IsWide();
            await foreach (var _ in ScreenSize.Changes(cancellationToken)) {
                var isWide = IsWide();
                if (lastIsWide != isWide) {
                    lastIsWide = isWide;
                    Left.SetIsVisible(Left.IsVisible.Value); // It changes to the right one anyway
                }
            }
        });
}
