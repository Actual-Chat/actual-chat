using ActualChat.App.Maui.Services;
using Activity = Android.App.Activity;

namespace ActualChat.App.Maui;

public class AndroidWebViewLivenessProbeAdapter
{
    private MauiWebViewLivenessProbe? _probe;
    private bool _hasMovedToBackground;

    public void OnResume(Activity activity)
    {
        if (!_hasMovedToBackground)
            return;
        _probe = new MauiWebViewLivenessProbe(MauiApplication.Current.Services);
        _ = _probe.StartCheck();
    }

    public void OnPause(Activity activity)
    {
        _hasMovedToBackground = true;
        _probe?.StopCheck();
        _probe = null;
    }
}
