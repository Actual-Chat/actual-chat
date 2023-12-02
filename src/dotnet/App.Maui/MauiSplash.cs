using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui;

#if USE_MAUI_SPLASH

public class MauiSplash : Grid
{
    private static readonly TimeSpan HalfLifeDuration = TimeSpan.FromSeconds(OSInfo.IsIOS ? 0.333 : 1);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5.5); // Must be > 5s (web loading overlay auto-removal time)
    private static readonly double MaxOpacity = 1;
    private static int _lastIndex;
    private readonly int _index = Interlocked.Increment(ref _lastIndex);
    private readonly ProgressBar _progressBar;
    private readonly Image _logo;

    public MauiSplash()
    {
        _logo = new Image {
            WidthRequest = 200,
            HeightRequest = 200,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Source = "splashscreen.png",
        };
        _progressBar = new ProgressBar {
            HorizontalOptions = LayoutOptions.Fill,
            ProgressColor = Colors.White,
            WidthRequest = 200,
        };

        ZIndex = 1;
        BackgroundColor = MauiSettings.SplashBackgroundColor;
        VerticalOptions = LayoutOptions.Fill;
        HorizontalOptions = LayoutOptions.Fill;
        SetOpacity(MaxOpacity);

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
        var isFirstSplash = _index == 1;
        var whenSplashRemoved = isFirstSplash
            ? MauiLoadingUI.WhenFirstSplashRemoved
            : MauiLoadingUI.WhenSplashRemoved();
        var isThemeApplied = false;
        try {
            await Animate(t => {
                if (t >= Timeout || whenSplashRemoved.IsCompleted)
                    return false;

                _progressBar.Progress = 1.0 - Math.Pow(0.5, t / HalfLifeDuration);
                return true;
            }).ConfigureAwait(true);
            await Animate(t => {
                if (t >= FadeDuration)
                    return false;

                SetOpacity(double.Lerp(MaxOpacity, 0, t / FadeDuration).Clamp(0, MaxOpacity));
                if (!isThemeApplied && t * 2 > FadeDuration) {
                    isThemeApplied = true;
                    MauiThemeHandler.Instance.Apply(true);
                }
                return true;
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) {
            DefaultLog.LogWarning(e, "Failed to animate MauiSplash");
        }
        finally {
            (Parent as Layout)!.Remove(this);
            if (!isThemeApplied)
                MauiThemeHandler.Instance.Apply(true);
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

#endif
