using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using Activity = Android.App.Activity;

namespace ActualChat.App.Maui;

public class AndroidBatteryOptimizationHandler : BatteryOptimizationHandler
{
    private const int RequestCode = 810;

    public AndroidBatteryOptimizationHandler(UIHub hub, bool mustStart = true)
        : base(hub, false)
    {
        ExpirationPeriod = null;
        if (mustStart)
            this.Start();
    }

    protected override Task<bool?> Get(CancellationToken cancellationToken)
    {
        var context = Platform.AppContext;
        var powerManager = (PowerManager?)context.GetSystemService(Context.PowerService);
        var isExempt = powerManager?.IsIgnoringBatteryOptimizations(context.PackageName!);
        return Task.FromResult(isExempt);
    }

    protected override async Task<bool> Request(CancellationToken cancellationToken)
    {
        if (Platform.CurrentActivity is not { } activity)
            return false;

        var whenClosedSource = TaskCompletionSourceExt.New<bool>();
        AndroidActivityResultHandlers.Register(OnActivityResult);
        try {
            var intent = new Intent(
                Settings.ActionRequestIgnoreBatteryOptimizations,
                Android.Net.Uri.Parse("package:" + activity.PackageName));
            activity.StartActivityForResult(intent, RequestCode);
            await whenClosedSource.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        finally {
            AndroidActivityResultHandlers.Unregister(OnActivityResult);
        }
        return await Get(cancellationToken).ConfigureAwait(false) ?? false;

        void OnActivityResult(Activity _, int requestCode, Result result, Intent? data) {
            if (requestCode == RequestCode)
                whenClosedSource.TrySetResult(true);
        }
    }

    protected override Task Troubleshoot(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
