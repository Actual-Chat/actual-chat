using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Notification.UI.Blazor;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Android.Content;
using Microsoft.JSInterop;
using Microsoft.Maui.LifecycleEvents;
using Activity = Android.App.Activity;
using Result = Android.App.Result;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial void AddPlatformServices(this IServiceCollection services)
    {
        services.AddSingleton<Java.Util.Concurrent.IExecutorService>(_ =>
            Java.Util.Concurrent.Executors.NewWorkStealingPool()!);

        services.AddSingleton<AndroidContentDownloader>();
        services.AddAlias<IIncomingShareFileDownloader, AndroidContentDownloader>();
        services.AddScoped<IVisualMediaViewerFileDownloader, AndroidVisualMediaViewerFileDownloader>();

        services.AddTransient<IDeviceTokenRetriever>(c => new AndroidDeviceTokenRetriever(c));
        // Temporarily disabled switch between loud speaker and earpiece
        // to have single audio channel controlled with volume buttons
        //services.AddScoped<IAudioOutputController>(c => new AndroidAudioOutputController(c));
        services.AddScoped<INotificationsPermission>(c => new AndroidNotificationsPermission(c));
        services.AddScoped<IRecordingPermissionRequester>(_ => new AndroidRecordingPermissionRequester());
        services.AddScoped(c => new NativeGoogleAuth(c));
    }

    private static partial void AddPlatformServicesToSkip(HashSet<Type> servicesToSkip)
    {
        servicesToSkip.Add(typeof(Android.Views.LayoutInflater));
        servicesToSkip.Add(typeof(AndroidX.Fragment.App.FragmentManager));
    }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
        => events.AddAndroid(android => {
            var livenessProbeAdapter = new AndroidWebViewLivenessProbeAdapter();
            var incomingShare = new IncomingShareHandler();
            android.OnPostCreate(incomingShare.OnPostCreate);
            android.OnNewIntent(incomingShare.OnNewIntent);
            android.OnResume(livenessProbeAdapter.OnResume);
            android.OnPause(livenessProbeAdapter.OnPause);
            android.OnActivityResult(OnActivityResult);
            android.OnBackPressed(activity => {
                _ = HandleBackPressed(activity);
                return true;
            });
        });

    private static void OnActivityResult(Activity activity, int requestCode, Result resultCode, Intent? data)
        => AndroidActivityResultHandlers.Invoke(activity, requestCode, resultCode, data);

    private static async Task HandleBackPressed(Android.App.Activity activity)
    {
        var handler = ScopedServices.GetService<BackButtonHandler>();
        if (handler != null) {
            var eventArgs = new BackPressedEventArgs(MoveToBack);
            handler.OnBackPressed(eventArgs);
            if (eventArgs.Handled)
                return;
        }
        var webView = MainPage.Current!.PlatformWebView;
        var goBack = webView != null && await TryGoBack(webView).ConfigureAwait(false);
        if (goBack)
            return;
        MoveToBack();

        void MoveToBack()
            // Move app to background as Home button acts.
            // It prevents scenario when app is running, but activity is destroyed.
            => activity.MoveTaskToBack(true);
    }

    private static async Task<bool> TryGoBack(Android.Webkit.WebView webView)
    {
        var canGoBack = webView.CanGoBack();
        if (canGoBack) {
            webView.GoBack();
            return true;
        }
        // Sometimes Chromium reports that it can't go back while there are 2 items in the history.
        // It seems that this bug exists for a while, not fixed yet and there is not plans to do it.
        // https://bugs.chromium.org/p/chromium/issues/detail?id=1098388
        // https://github.com/flutter/flutter/issues/59185
        // But we can use web api to navigate back.
        var list = webView.CopyBackForwardList();
        var canGoBack2 = list is { Size: > 1, CurrentIndex: > 0 };
        if (!canGoBack2 || !TryGetScopedServices(out var scopedServices))
            return false;

        var js = scopedServices.JSRuntime();
        await js.InvokeVoidAsync("history.back").ConfigureAwait(false);
        return true;
    }
}
