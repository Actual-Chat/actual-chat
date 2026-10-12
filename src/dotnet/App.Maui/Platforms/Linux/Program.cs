using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace ActualChat.App.Maui;

public sealed class Program : GtkMauiApplication
{
    // LinuxDesktopEntry writes it instead: the labs entry has neither %u nor a MimeType,
    // so the desktop couldn't hand voxt:// links to the app
    protected override bool CreateDesktopEntry => false;

    // This is the main entry point of the application.
    public static void Main(string[] args)
    {
        if (!LinuxActivation.TryStart(args))
            return;

        LinuxEssentialsDefaults.Apply(); // TODO(maui-labs): see LinuxEssentialsDefaults
        // TODO(maui-labs): drop with the next labs package - main runs the GTK application with one.
        // Without a synchronization context an await on the UI thread resumes on the thread pool,
        // and the code after it - a WebView recreation, say - touches GTK from the wrong thread.
        SynchronizationContext.SetSynchronizationContext(new GLib.Internal.MainLoopSynchronizationContext());
        // No arguments for GApplication: it would try to open a link as a file, and it rejects
        // the options MauiProgram reads from the command line itself
        new Program().Run([]);
    }

    protected override MauiApp CreateMauiApp()
        => MauiProgram.CreateMauiApp();

    protected override void OnStarted()
    {
        WindowConfigurator.Configure();
        LinuxDesktopEntry.Ensure(ApplicationId);
        LinuxDeviceNotifications.AddOpenAction();
    }
}
