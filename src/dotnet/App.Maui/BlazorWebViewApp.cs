using ActualLab.Internal;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace ActualChat.App.Maui;

public class BlazorWebViewApp
{
    private static readonly object Lock = new();
    private static Func<Task<BlazorWebViewApp>>? _initializeFactory;
    private static Task? _startupTask;
    private static BlazorWebViewApp? _current;
    private static ILogger? _log; // Otherwise Rider assumes we're referencing it from elsewhere
    // ReSharper disable once InconsistentNaming
    private static readonly TaskCompletionSource<BlazorWebViewApp> _currentSource =
        TaskCompletionSourceExt.New<BlazorWebViewApp>();
    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger<BlazorWebViewApp>();

    public static BlazorWebViewApp Current {
        get => _current ?? throw Errors.NotInitialized(nameof(Current));
        private set {
            lock (Lock) {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                if (ReferenceEquals(_current, value))
                    return;
                if (_current != null)
                    throw Errors.AlreadyInitialized(nameof(Current));

                _current = value;
                _currentSource.TrySetResult(value);
                Log.LogInformation("BlazorWebViewApp ready");
            }
        }
    }

    public static Task<BlazorWebViewApp> WhenAppReady
        => _currentSource.Task;

    public IServiceProvider Services { get; private set; }

    internal BlazorWebViewApp(IServiceProvider services)
        => Services = services;

    public static BlazorWebViewAppBuilder CreateBuilder()
        => new ();

    public static void Initialize(Func<Task<BlazorWebViewApp>> initializeFactory)
    {
        lock (Lock) {
            if (_initializeFactory != null)
                throw new InvalidOperationException("The BlazorWebViewApp has already been initialized.");
            _initializeFactory = initializeFactory;
            Log.LogInformation("BlazorWebViewApp has initialized");
        }
    }

    public static void EnsureStarted()
    {
        lock (Lock) {
            if (_startupTask != null)
                return;

            if (_initializeFactory == null)
                throw new InvalidOperationException("The BlazorWebViewApp has to be initialized first.");

            Log.LogInformation("BlazorWebViewApp start has been requested");

            _startupTask = Task.Run(async () => {
                Current = await _initializeFactory().ConfigureAwait(false);
            });
        }
    }
}

public class BlazorWebViewAppBuilder
{
    private readonly ServiceCollection _services = new();
    public IServiceCollection Services => _services;

    public BlazorWebViewApp Build()
    {
        IServiceProvider serviceProvider = _services.BuildServiceProvider();
        // Mark the service collection as read-only to prevent future modifications
        _services.MakeReadOnly();
        return new BlazorWebViewApp(serviceProvider);
    }
}
