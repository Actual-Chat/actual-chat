using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class SplashOverlay : Grid
{
    private static readonly TimeSpan ExpectedRenderDuration = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(0.15);
    private static readonly double RenderPart = ExpectedRenderDuration / (ExpectedRenderDuration + FadeDuration);
    private static readonly double FadePart = 1 - RenderPart;
    private static readonly TimeSpan SplashTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.05);
    private static readonly double MaxOpacity = 0.99;
    private readonly ProgressBar _progressBar;
    private readonly Image _logo;
    private LoadingUI? _loadingUI;
    private MomentClockSet? _clocks;

    private IServiceProvider Services { get; set; } = null!;
    private LoadingUI LoadingUI => _loadingUI ??= Services.GetRequiredService<LoadingUI>();
    private MomentClockSet Clocks => _clocks ??= Services.Clocks();

    public SplashOverlay()
    {
        _progressBar = new ProgressBar {
            HorizontalOptions = LayoutOptions.Fill,
            ProgressColor = Colors.White,
            WidthRequest = 200,
        };
        _logo = new Image {
            WidthRequest = 200,
            HeightRequest = 200,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Source = "splashscreen.png",
        };

        ZIndex = 1;
        Opacity = MaxOpacity;
        BackgroundColor = MauiSettings.SplashBackgroundColor;
        VerticalOptions = LayoutOptions.Fill;
        HorizontalOptions = LayoutOptions.Fill;
        Add(_logo);
        Add(_progressBar);

        _ = AnimateSplash();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        var statusBarHeight = Bars.Instance.GetStatusBarHeight();
        var bottomBarHeight = Window!.Height - MainPage.Current.Height - statusBarHeight;
        var offset = (bottomBarHeight - statusBarHeight) / 2;
        _logo.Margin = new(0, offset, 0, 0);
        _progressBar.Margin = new(0, 200 + offset, 0, 0);
    }

    private async Task AnimateSplash()
    {
        try {
            Services = await WhenScopedServicesReady().ConfigureAwait(true);
            LoadingUI.RemoveLoadingOverlay();
            await UpdateLoop(ExpectedRenderDuration,
                progress => {
                    if (LoadingUI.WhenRendered.IsCompleted)
                        return false;

                    _progressBar.Progress = progress * RenderPart;
                    return true;
                }).ConfigureAwait(true);
            if (!LoadingUI.WhenRendered.IsCompleted)
                await LoadingUI.WhenRendered.WaitAsync(SplashTimeout - ExpectedRenderDuration).ConfigureAwait(true);
            await UpdateLoop(FadeDuration,
                progress => {
                    _progressBar.Progress = (progress * FadePart) + RenderPart;
                    Opacity = (1 - progress).Clamp(0, MaxOpacity);
                    return true;
                }).ConfigureAwait(true);
            _progressBar.Progress = 1;
            Opacity = 0;
            LoadingUI.MarkSplashOverlayHidden();
        }
        catch(OperationCanceledException) { }
        catch (Exception e) {
            DefaultLog.LogCritical(e, "Failed to show splash screen");
        }
        finally {
            (Parent as Layout)!.Remove(this);
        }
    }

    private async Task UpdateLoop(TimeSpan totalDuration, Func<double, bool> uiAction)
    {
        var clock = Clocks.CpuClock;
        var startedAt = clock.Now;
        var steps = totalDuration / UpdateInterval;
        for (int i = 1; i <= steps; i++) {
            var stopAt = startedAt + (i * UpdateInterval);
            if (!uiAction(i / steps))
                return;
            await clock.Delay(stopAt).ConfigureAwait(true);
        }
    }
}