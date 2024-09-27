using Android.Content;
using Android.OS;
using Microsoft.Maui.LifecycleEvents;
using Activity = Android.App.Activity;

namespace ActualChat.App.Maui;

public static class IntentHandler
{
    private static readonly ILogger Log = StaticLog.For(typeof(IntentHandler));
    private static Intent? _startIntent;
    private static bool _hasResumedOnce;

    public static void Activate(IAndroidLifecycleBuilder android)
    {
        android.OnCreate(OnCreate);
        android.OnNewIntent(OnNewIntent);
        android.OnResume(OnResume);
    }

    private static void OnCreate(Activity activity, Bundle? savedInstanceState)
    {
        var intent = activity.Intent;
        if (intent is null || OrdinalEquals(intent.Action, Intent.ActionMain))
            return;

        if (intent.IsFromHistory()) {
            Log.LogDebug("Intent is from history; skipping it");
            return;
        }

        if (!_hasResumedOnce) {
            Log.LogDebug("Postponing activity start intent handling until resuming");
            _startIntent = intent;
        }
        else {
            Log.LogDebug("About to handle activity intent");
            HandleIntent(intent);
        }
    }

    private static void OnNewIntent(Activity activity, Intent? intent)
    {
        if (intent is not null) {
            Log.LogDebug("About to handle new intent");
            HandleIntent(intent);
        }
    }

    private static void OnResume(Activity activity)
    {
        var hasResumedOnce = _hasResumedOnce;
        _hasResumedOnce = true;
        if (!hasResumedOnce && _startIntent is not null) {
            Log.LogDebug("About to handle start intent");
            HandleIntent(_startIntent);
        }
    }

    private static void HandleIntent(Intent intent)
    {
        if (_startIntent is not null) {
            _startIntent = null;
            Log.LogDebug("Clear start intent");
        }

        IncomingShareHandler.HandleIntent(intent);
        NotificationHandler.HandleIntent(intent);
    }
}
