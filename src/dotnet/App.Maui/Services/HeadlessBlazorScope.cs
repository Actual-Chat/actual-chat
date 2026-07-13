namespace ActualChat.App.Maui.Services;

/// <summary>
/// A private DI scope over the app container for wake-driven headless audio playback.
/// Never published via <see cref="AppServicesAccessor"/>; the WebView scope always wins.
/// </summary>
public sealed class HeadlessBlazorScope : IAsyncDisposable
{
    private static readonly Lock StaticLock = new();
    private static volatile HeadlessBlazorScope? _current;
    private static ILogger Log => field ??= StaticLog.For<HeadlessBlazorScope>();

    private readonly IServiceScope _scope;

    public static HeadlessBlazorScope? Current => _current;

    public IServiceProvider Services => _scope.ServiceProvider;

    private HeadlessBlazorScope(IServiceScope scope)
        => _scope = scope;

    public static HeadlessBlazorScope? GetOrCreate()
    {
        lock (StaticLock) {
            if (AppServicesAccessor.TryGetScopedServices(out _))
                return null;

            if (_current is not null)
                return _current;

            var scope = BlazorWebViewApp.Current.Services.CreateScope();
            // No WebView will ever attach here: make every JS call fail with the
            // JSRuntimeDisconnected the UI code already tolerates (the page-reload path).
            scope.ServiceProvider.GetRequiredService<SafeJSRuntime>().MarkDisconnected();
            _current = new HeadlessBlazorScope(scope);
            Log.LogInformation("Headless scope created");
            return _current;
        }
    }

    public static Task DisposeCurrent(string reason)
    {
        HeadlessBlazorScope? current;
        lock (StaticLock) {
            current = _current;
            _current = null;
        }
        if (current is null)
            return Task.CompletedTask;

        Log.LogInformation("Disposing headless scope ({Reason})", reason);
        return current.DisposeAsyncCore();
    }

    public ValueTask DisposeAsync()
    {
        lock (StaticLock)
            if (_current == this)
                _current = null;
        return new ValueTask(DisposeAsyncCore());
    }

    // Private methods

    private async Task DisposeAsyncCore()
    {
        if (_scope is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else
            _scope.Dispose();
    }
}
