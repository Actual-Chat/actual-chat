using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.UI.Xaml;

namespace ActualChat.App.Maui.WinUI;

public partial class App
{
    private static readonly TimeSpan RedirectTimeout = TimeSpan.FromSeconds(3);

    public static event Action<string> AppInstanceActivated = _ => { };

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ = DecideRedirection().ContinueWith(t => {
            if (t.GetAwaiter().GetResult())
                Environment.Exit(0);
            else
                WindowsSplashScreen.Show(() => base.OnLaunched(args));
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task<bool> DecideRedirection()
    {
        var appInstance = AppInstance.FindOrRegisterForKey("sharedKey");
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (!appInstance.IsCurrent && await TryRedirect(appInstance, args).ConfigureAwait(true))
            return true;

        // Nobody took the activation: the instance with this key is stuck, or there is none and another
        // build holds the data folder (the key only matches within one package identity)
        if (!await WindowsInstanceGuard.TakeOver().ConfigureAwait(true))
            return true;

        appInstance = AppInstance.FindOrRegisterForKey("sharedKey");
        if (!appInstance.IsCurrent)
            return true;

        appInstance.Activated += OnAppInstanceActivated;
        WindowsNotificationActivation.Register();
        // A toast clicked while the app wasn't running starts it with the click as the activation
        if (args.Data is AppNotificationActivatedEventArgs toastArgs)
            WindowsNotificationActivation.Handle(toastArgs);
        return false;
    }

    private static async Task<bool> TryRedirect(AppInstance appInstance, AppActivationArguments args)
    {
        try {
            await appInstance.RedirectActivationToAsync(args).AsTask().WaitAsync(RedirectTimeout).ConfigureAwait(true);
            return true;
        }
        catch (TimeoutException) {
            return false;
        }
    }

    private void OnAppInstanceActivated(object? sender, AppActivationArguments e)
    {
        if (e.Data is AppNotificationActivatedEventArgs toastArgs) {
            WindowsNotificationActivation.Handle(toastArgs);
            return;
        }

        var arguments = e.Data switch {
            Windows.ApplicationModel.Activation.LaunchActivatedEventArgs x => x.Arguments,
            Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs x => x.Uri.AbsoluteUri,
            _ => null,
        };
        if (arguments != null)
            AppInstanceActivated.Invoke(arguments);
    }
}
