using Stl.Internal;

namespace ActualChat.App.Maui;

public static class ScopedServicesAccessor
{
    private static readonly object _lock = new();
    private static IServiceProvider? _scopedServices;
    private static readonly TaskSource<Unit> _whenInitializedSource = TaskSource.New<Unit>(true);

    public static bool IsInitialized
        => _scopedServices != null;

    public static Task WhenInitialized
        => _whenInitializedSource.Task;

    public static IServiceProvider ScopedServices {
        get {
            if (_scopedServices == null)
                throw Errors.NotInitialized(nameof(ScopedServices));
            return _scopedServices;
        }
        set {
            lock(_lock) {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                if (_scopedServices != null && !ReferenceEquals(_scopedServices, value))
                    throw Errors.AlreadyInitialized(nameof(ScopedServices));
#if ANDROID
                AppServices.LogFor("ScopedServicesAccessor").LogDebug("ScopedServicesAccessor.Initialize. IsInitialized: {IsInitialized}", IsInitialized);
#endif
                _scopedServices = value;
                _whenInitializedSource.TrySetResult(default);
            }
        }
    }
}
