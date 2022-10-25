using Android.Util;

namespace ActualChat.App.Maui;

public static class ScopedServiceLocator
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services {
        get {
            if (_services == null)
                throw StandardError.Constraint("ServiceLocator is not initialized yet.");
            return _services;
        }
    }

    public static bool IsInitialized => _services != null;

    public static void Initialize(IServiceProvider services)
    {
#if ANDROID
        Log.Debug(AndroidConstants.LogTag, $"ScopedServiceLocator.Initialize. IsInitialzied: {IsInitialized}");
#endif
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }
}
