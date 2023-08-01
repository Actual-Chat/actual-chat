using ActualChat.App.Maui.Services;
using Activity = Android.App.Activity;

namespace ActualChat.App.Maui;

public class AndroidWebViewLivenessProbeAdapter
{
    private MauiWebViewLivenessProbe? _probe;

    public void OnResume(Activity activity)
    {
        _probe = new MauiWebViewLivenessProbe(MauiApplication.Current.Services);
        _ = _probe.StartCheck();
    }

    public void OnPause(Activity activity)
    {
        _probe?.StopCheck();
        _probe = null;
    }
}
