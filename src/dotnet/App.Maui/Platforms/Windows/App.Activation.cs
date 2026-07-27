using Microsoft.Windows.AppLifecycle;
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
                base.OnLaunched(args);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private async Task<bool> DecideRedirection()
    {
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        var appInstance = AppInstance.FindOrRegisterForKey("sharedKey");
        if (appInstance.IsCurrent) {
            appInstance.Activated += OnAppInstanceActivated;
            return false;
        }
        await appInstance.RedirectActivationToAsync(args);
        return true;
    }

    private void OnAppInstanceActivated(object? sender, AppActivationArguments e)
    {
        var arguments = e.Data switch {
            Windows.ApplicationModel.Activation.LaunchActivatedEventArgs x => x.Arguments,
            Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs x => x.Uri.AbsoluteUri,
            _ => null,
        };
        if (arguments != null)
            AppInstanceActivated.Invoke(arguments);
    }
}
