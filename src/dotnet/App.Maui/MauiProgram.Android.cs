using ActualChat.App.Maui.Services;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Notification.UI.Blazor;
using ActualChat.Streaming.UI.Blazor.Services;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Microsoft.JSInterop;
using Microsoft.Maui.LifecycleEvents;
using Activity = Android.App.Activity;

namespace ActualChat.App.Maui;

public static partial class MauiProgram
{
    private static partial void AddPlatformServices(this IServiceCollection services)
    {
        services.AddSingleton<Java.Util.Concurrent.IExecutorService>(_ =>
            Java.Util.Concurrent.Executors.NewWorkStealingPool()!);

        services.AddSingleton<IHistoryExitHandler>(_ => new AndroidHistoryExitHandler());
        services.AddSingleton<AndroidContentDownloader>();
        services.AddAlias<IIncomingShareFileDownloader, AndroidContentDownloader>();
        services.AddScoped<IMediaSaver, AndroidMediaSaver>();

        services.AddTransient<IDeviceTokenRetriever>(c => new AndroidDeviceTokenRetriever(c));
        // Temporarily disabled switch between loud speaker and earpiece
        // to have single audio channel controlled with volume buttons
        //services.AddScoped<IAudioOutputController>(c => new AndroidAudioOutputController(c));
        services.AddScoped<INotificationsPermission>(c => new AndroidNotificationsPermission(c));
        services.AddScoped<IRecordingPermissionRequester>(_ => new AndroidRecordingPermissionRequester());
        services.AddSingleton(c => new NativeGoogleAuth(c));
        services.AddSingleton<Action<ThemeInfo>>(_ => MauiThemeHandler.Instance.OnThemeChanged);
    }

    private static partial void AddPlatformServicesToSkip(HashSet<Type> servicesToSkip)
    {
        servicesToSkip.Add(typeof(Android.Views.LayoutInflater));
        servicesToSkip.Add(typeof(AndroidX.Fragment.App.FragmentManager));
    }

    private static partial void ConfigurePlatformLifecycleEvents(ILifecycleBuilder events)
        => events.AddAndroid(android => {
            AndroidLifecycleLogger.Activate(android);
            var incomingShare = new IncomingShareHandler();
            android.OnPostCreate(incomingShare.OnPostCreate);
            android.OnNewIntent(incomingShare.OnNewIntent);
            android.OnResume(_ => MauiWebView.LogResume());
            android.OnPause(_ => MauiLivenessProbe.CancelCheck());
            android.OnActivityResult(AndroidActivityResultHandlers.Invoke);
            android.OnBackPressed(activity => {
                _ = OnBackPressed(activity);
                return true; // We handle it in HandleBackPressed
            });
        });

    private static async Task OnBackPressed(Activity activity)
    {
        var couldStepBack = await DispatchToBlazor(c => c.GetRequiredService<History>().TryStepBack()).ConfigureAwait(true);
        if (!couldStepBack)
            activity.MoveTaskToBack(true);
    }
}
