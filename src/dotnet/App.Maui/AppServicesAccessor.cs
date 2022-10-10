namespace ActualChat.App.Maui;

public static class AppServicesAccessor
{
    private static readonly object _lock = new();
    private static IServiceProvider? _appServices;

    public static IServiceProvider AppServices {
        get {
            if (_appServices == null)
                throw Errors.NotInitialized(nameof(AppServices));
            return _appServices;
        }
        set {
            lock (_lock) {
                if (_appServices != null)
                    throw Errors.AlreadyInitialized(nameof(AppServices));
                _appServices = value ?? throw new ArgumentNullException(nameof(value));
            }
        }
    }
}
