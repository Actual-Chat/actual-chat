using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.UI.Xaml;

namespace ActualChat.App.Maui.WinUI;

public partial class App
{
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
        if (appInstance.IsCurrent) {
            appInstance.Activated += OnAppInstanceActivated;
            WindowsAppNotifications.Register();
            // A toast clicked while the app wasn't running starts it with the click as the activation
            if (AppInstance.GetCurrent().GetActivatedEventArgs().Data is AppNotificationActivatedEventArgs toastArgs)
                WindowsAppNotifications.Handle(toastArgs);
            return false;
        }
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        await appInstance.RedirectActivationToAsync(args);
        return true;
    }

    private void OnAppInstanceActivated(object? sender, AppActivationArguments e)
    {
        if (e.Data is AppNotificationActivatedEventArgs toastArgs) {
            WindowsAppNotifications.Handle(toastArgs);
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
