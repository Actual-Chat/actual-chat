using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

public class MauiSplash : Grid
{
    private static readonly TimeSpan HalfLifeDuration = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5.5); // Must be > 5s (web loading overlay auto-removal time)
    private static readonly double MaxOpacity = 0.99;
    private readonly ProgressBar _progressBar;
    private readonly Image _logo;
    private LoadingUI? _scopedLoadingUI;

    private LoadingUI? ScopedLoadingUI {
        get {
            if (_scopedLoadingUI != null)
                return _scopedLoadingUI;

            _scopedLoadingUI = TryGetScopedServices(out var services) ? services.GetRequiredService<LoadingUI>() : null;
            _scopedLoadingUI?.RemoveLoadingOverlay(true);
            return _scopedLoadingUI;
        }
    }

    public MauiSplash()
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
        SetOpacity(MaxOpacity);
        BackgroundColor = MauiSettings.SplashBackgroundColor;
        VerticalOptions = LayoutOptions.Fill;
        HorizontalOptions = LayoutOptions.Fill;
        Add(_logo);
        Add(_progressBar);
        _ = Animate();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        var statusBarHeight = Bars.Instance.GetStatusBarHeight();
        var bottomBarHeight = Window!.Height - MainPage.Current.Height - statusBarHeight;
        var offset = (bottomBarHeight - statusBarHeight) / 2;
        _logo.Margin = new(0, offset, 0, 0);
        _progressBar.Margin = new(0, 200 + offset, 0, 0);
    }

    private async Task Animate()
    {
        LoadingUI.IsMauiSplashShown = true;
        MauiThemeHandler.Instance.Apply();
        try {
            await Animate(t => {
                if (t >= Timeout || ScopedLoadingUI is { WhenRendered.IsCompleted: true })
                    return false;

                _progressBar.Progress = 1.0 - Math.Pow(0.5, t / HalfLifeDuration);
                return true;
            }).ConfigureAwait(true);
            await Animate(t => {
                if (t >= FadeDuration)
                    return false;

                SetOpacity(double.Lerp(MaxOpacity, 0, t / FadeDuration).Clamp(0, MaxOpacity));
                return true;
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) {
            DefaultLog.LogWarning(e, "Failed to animate splash");
        }
        finally {
            (Parent as Layout)!.Remove(this);
            LoadingUI.IsMauiSplashShown = false;
            MauiThemeHandler.Instance.Apply();
        }
    }

    private void SetOpacity(double opacity)
    {
        var controlOpacity = Math.Pow(opacity, 4);
        if (controlOpacity < 0.1)
            controlOpacity = 0;

        _logo.Opacity = controlOpacity;
        _progressBar.Opacity = controlOpacity;
        Opacity = opacity;
    }

    private static async Task Animate(Func<TimeSpan, bool> frameAction, double fps = 30)
    {
        var frameDuration = TimeSpan.FromSeconds(1) / fps;
        var startedAt = CpuTimestamp.Now;
        while (true) {
            if (!frameAction.Invoke(CpuTimestamp.Now - startedAt))
                return;

            await Task.Delay(frameDuration).ConfigureAwait(true);
        }
    }
}
