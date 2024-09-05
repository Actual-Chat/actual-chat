using System.Diagnostics.CodeAnalysis;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Services;

[method: DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MauiReloadUI))]
public class MauiReloadUI(IServiceProvider services) : ReloadUI(services)
{
    public override void Reload(bool clearCaches = false, bool clearLocalSettings = false)
    {
        Log.LogInformation("Reload requested");
        _ = MainThreadExt.InvokeLaterAsync(async () => {
            Log.LogInformation("Reloading...");
            try {
                await Clear(clearCaches, clearLocalSettings).ConfigureAwait(true);
                MainPage.Current.RecreateWebView();
            }
            catch (Exception e) {
                Log.LogError(e, "Reload failed, terminating");
                Quit(); // We can't do much in this case
            }
        });
    }

    public override void Quit()
        => App.Current.Quit();
}
