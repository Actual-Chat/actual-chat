using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class SplashOverlay : Grid
{
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
            var fadePct = 0.2;
            await UpdateLoop(TimeSpan.FromSeconds(1.5),
                    TimeSpan.FromSeconds(0.1),
                    progress => {
                        if (LoadingUI.WhenAppRendered.IsCompleted)
                            return false;

                        progressBar.Progress = progress * (1 - fadePct);
                        return true;
                    })
                .ConfigureAwait(true);
            await LoadingUI.WhenAppRendered.ConfigureAwait(true);
            await UpdateLoop(TimeSpan.FromSeconds(0.5),
                    TimeSpan.FromSeconds(0.05),
                    progress => {
                        progressBar.Progress = (progress * fadePct) + (1 - fadePct);
                        Opacity = 1 - progress;
                        return true;
                    })
                .ConfigureAwait(true);
            progressBar.Progress = 1;
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
            await clock.Delay(stopAt).ConfigureAwait(true);
            if (!uiAction(i / steps))
                return;
        }
    }
}
