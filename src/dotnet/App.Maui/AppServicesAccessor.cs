using System.Diagnostics.CodeAnalysis;
using ActualChat.App.Maui.Services;
using Microsoft.JSInterop;
using Stl.Internal;

namespace ActualChat.App.Maui;

public class AppServicesAccessor
{
    private static readonly object _lock = new();
    private static ILogger? _log;
    private static volatile MauiAppSettings? _appSettings;
    private static volatile IServiceProvider? _appServices;
    private static volatile IServiceProvider? _scopedServices;
    private static volatile TaskCompletionSource<Unit> _whenScopedServicesReadySource = TaskCompletionSourceExt.New<Unit>();

    private static ILogger Log => _log ??= MauiDiagnostics.LoggerFactory.CreateLogger<AppServicesAccessor>();

    public static Task WhenScopedServicesReady => _whenScopedServicesReadySource.Task;

    public static MauiAppSettings AppSettings {
        get => _appSettings ?? throw Errors.NotInitialized(nameof(AppSettings));
        set {
            lock (_lock) {
                if (value == null!)
                    throw new ArgumentNullException(nameof(value));

                _appSettings = value;
            }
        }
    }

    public static IServiceProvider AppServices {
        get => _appServices ?? throw Errors.NotInitialized(nameof(AppServices));
        set {
            lock (_lock) {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                if (ReferenceEquals(_appServices, value))
                    return;
                if (_appServices != null)
                    throw Errors.AlreadyInitialized(nameof(AppServices));

                _appServices = value;
                Log.LogDebug("AppServices ready");
            }
        }
    }

    public static IServiceProvider ScopedServices {
        get => _scopedServices ?? throw Errors.NotInitialized(nameof(ScopedServices));
        set {
            lock (_lock) {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                if (ReferenceEquals(_scopedServices, value))
                    return;
                if (_scopedServices != null)
                    throw Errors.AlreadyInitialized(nameof(ScopedServices));

                _scopedServices = value;
                _whenScopedServicesReadySource.TrySetResult(default);
                Log.LogDebug("ScopedServices ready");
            }
        }
    }

    public static bool TryGetScopedServices([NotNullWhen(true)] out IServiceProvider? scopedServices)
    {
        scopedServices = _scopedServices;
        return scopedServices != null;
    }

    public static void DiscardScopedServices()
    {
        lock (_lock) {
            if (_scopedServices == null)
                return;

            var js = _scopedServices.GetRequiredService<IJSRuntime>();
            _whenScopedServicesReadySource = TaskCompletionSourceExt.New<Unit>(); // Must go first
            _scopedServices = null;
            JSObjectReferenceDisconnectHelper.MarkAsDisconnected(js);
            AppServices.LogFor(nameof(AppServicesAccessor)).LogDebug("ScopedServices discarded");
        }
    }
}
