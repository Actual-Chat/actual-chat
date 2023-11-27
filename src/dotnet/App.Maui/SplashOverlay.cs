using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class SplashOverlay : Grid
{
    private static readonly TimeSpan ExpectedRenderDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(0.5);
    private static readonly double RenderPart = ExpectedRenderDuration / (ExpectedRenderDuration + FadeDuration);
    private static readonly double FadePart = 1 - RenderPart;
    private static readonly TimeSpan SplashTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.05);

    public SplashOverlay()
    {
        var progressBar = new ProgressBar {
            HorizontalOptions = LayoutOptions.Fill,
            ProgressColor = Colors.White,
            WidthRequest = 200,
            Margin = new (0,200,0,0),
        };

        ZIndex = 1;
        Opacity = 0.99;
        BackgroundColor = Color.FromArgb("#0036A3");
        VerticalOptions = LayoutOptions.Fill;
        HorizontalOptions = LayoutOptions.Fill;
        Add(new Image {
            WidthRequest = 200,
            HeightRequest = 200,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Source = "splashscreen.png",
        });
        Add(progressBar);
        _ = AnimateSplash(progressBar);
    }

    private async Task AnimateSplash(ProgressBar progressBar)
    {
        try {
            await UpdateLoop(ExpectedRenderDuration,
                    UpdateInterval,
                    progress => {
                        if (LoadingUI.WhenAppRendered.IsCompleted)
                            return false;

                        progressBar.Progress = progress * RenderPart;
                        return true;
                    })
                .ConfigureAwait(true);
            if (!LoadingUI.WhenAppRendered.IsCompleted)
                await LoadingUI.WhenAppRendered.WaitAsync(SplashTimeout - ExpectedRenderDuration).ConfigureAwait(true);
            await UpdateLoop(TimeSpan.FromSeconds(0.5),
                    UpdateInterval,
                    progress => {
                        progressBar.Progress = (progress * FadePart) + RenderPart;
                        // Opacity = 1 - progress;
                        return true;
                    })
                .ConfigureAwait(true);
            progressBar.Progress = 1;
            // Opacity = 0;
        }
        catch(OperationCanceledException) { }
        catch (Exception e) {
            DefaultLog.LogCritical(e, "Failed to show splash screen");
        }
        finally {
            (Parent as Layout)!.Remove(this);
        }
    }

    private static async Task UpdateLoop(TimeSpan totalDuration, TimeSpan interval, Func<double, bool> uiAction)
    {
        var services = await WhenScopedServicesReady().ConfigureAwait(true);
        var clock = services.Clocks().CpuClock;
        var startedAt = clock.Now;
        var steps = totalDuration / interval;
        for (int i = 1; i <= steps; i++) {
            var stopAt = startedAt + (i * interval);
            if (!uiAction(i / steps))
                return;
            await clock.Delay(stopAt).ConfigureAwait(true);
        }
    }
}
