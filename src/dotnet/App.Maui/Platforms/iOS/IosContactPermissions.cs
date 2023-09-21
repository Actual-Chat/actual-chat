using ActualChat.App.Maui.Services;
using ActualChat.Contacts.UI.Blazor.Services;
using Foundation;
using UIKit;

namespace ActualChat.App.Maui;

public class IosContactPermissions(IServiceProvider services) : MauiContactPermissions(services), IContactPermissions
{
    public Task OpenSettings()
        => Dispatcher.InvokeAsync(()
            => UIApplication.SharedApplication.OpenUrlAsync(new NSUrl(UIApplication.OpenSettingsUrlString),
                new UIApplicationOpenUrlOptions()));
}
