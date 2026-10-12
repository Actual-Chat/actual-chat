namespace ActualChat.App.Maui;

/// <summary>
/// Keeps the GTK app single-instance, the GApplication way: the first launch owns a name on the
/// session bus; a later one - from the launcher, or from the desktop handing over a voxt:// link -
/// finds the name taken, activates the owner's <c>open</c> action with its link and exits.
/// The owner shows its window and raises <see cref="UrlOpened"/>.
/// </summary>
internal static class LinuxActivation
{
    private const string OpenActionName = "open";

    // Raised for voxt:// activations; MauiWebAuthenticator completes its sign-in flow on the
    // auth-complete callback. UI thread only.
    public static event Action<string>? UrlOpened;

    private static Gio.Application? _application;
    private static ILogger Log => field ??= StaticLog.For(typeof(LinuxActivation));

    public static bool TryStart(string[] args)
    {
        Gio.Module.Initialize();
        // TODO(maui-labs): use the backend's own GApplication once a second activation of it
        // stops building a second MauiApp - hence a GApplication with an id of its own here
        var applicationId = $"{MauiSettings.ReverseDomain}.Activation";
        var application = Gio.Application.New(applicationId, Gio.ApplicationFlags.DefaultFlags);
        var openAction = Gio.SimpleAction.New(OpenActionName, GLib.VariantType.String);
        openAction.OnActivate += static (_, e) => OnOpen(e.Parameter?.GetString(out var length) ?? "");
        application.AddAction(openAction);
        // Without a session bus it registers as a primary instance as well
        application.Register(null);
        if (!application.GetIsRemote()) {
            _application = application;
            return true;
        }

        var url = args.FirstOrDefault(x => x.StartsWith($"{MauiSettings.AppScheme}:")) ?? "";
        application.ActivateAction(OpenActionName, GLib.Variant.NewString(url));
        // The call above only queues the message, and this process is about to exit
        application.GetDbusConnection()?.FlushSync(null);
        return false;
    }

    // Private methods

    private static void OnOpen(string url)
    {
        // The link itself stays out of the log: an auth callback is one of them
        Log.LogInformation("Activated by another launch, has link: {HasLink}", !url.IsNullOrEmpty());
        WindowConfigurator.ShowWindow();
        if (!url.IsNullOrEmpty())
            UrlOpened?.Invoke(url);
    }
}
