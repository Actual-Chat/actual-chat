namespace ActualChat.App.Maui;

public static class ServiceLocator
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services {
        get {
            if (_services == null)
                throw StandardError.Constraint("ServiceLocator is not initialized yet.");
            return _services;
        }
    }

    public static void Initialize(IServiceProvider services)
        => _services = services ?? throw new ArgumentNullException(nameof(services));
}
