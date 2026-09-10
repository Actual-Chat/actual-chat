
// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ActualChat.App.Maui.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => {
            var e = args.Exception;
            // MAUI serves every app-origin request from the package folder and lets whatever
            // OpenStreamForReadAsync throws escape as a fatal exception - see #4459.
            if (e.StackTrace?.Contains("WinUIWebViewManager") == true)
                args.Handled = true;

            StaticLog.For<App>().LogError(e, "Unhandled exception, Handled: {Handled}", args.Handled);
        };
    }

    protected override MauiApp CreateMauiApp()
        => MauiProgram.CreateMauiApp();
}
