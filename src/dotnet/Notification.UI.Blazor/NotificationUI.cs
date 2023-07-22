using ActualChat.Hosting;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Notification.UI.Blazor;

public class NotificationUI : ProcessorBase, INotificationUIBackend, INotificationPermissions
{
    private readonly IMutableState<PermissionState> _permissionState;
    private readonly TaskCompletionSource _whenPermissionStateReady = TaskCompletionSourceExt.New();
    private volatile Task<string?>? _registerDeviceTask;
    private History? _history;
    private AutoNavigationUI? _autoNavigationUI;
    private IDeviceTokenRetriever? _deviceTokenRetriever;
    private ILogger? _log;

    private ILogger Log => _log ??= Services.LogFor(GetType());

    private IServiceProvider Services { get; }
    private History History => _history ??= Services.GetRequiredService<History>();
    private AutoNavigationUI AutoNavigationUI => _autoNavigationUI ??= Services.GetRequiredService<AutoNavigationUI>();
    private IDeviceTokenRetriever DeviceTokenRetriever => _deviceTokenRetriever ??= Services.GetRequiredService<IDeviceTokenRetriever>();
    private Session Session => History.Session;
    private HostInfo HostInfo => History.HostInfo;
    private UrlMapper UrlMapper => History.UrlMapper;
    private IJSRuntime JS => History.JS;

    public IState<PermissionState> PermissionState => _permissionState;
    public Task WhenReady { get; }

    public NotificationUI(IServiceProvider services)
    {
        Services = services;

        var stateFactory = services.StateFactory();
        _permissionState = stateFactory.NewMutable(Blazor.PermissionState.Denied, nameof(PermissionState));
        WhenReady = Initialize();

        async Task Initialize() {
            if (HostInfo.AppKind is AppKind.WebServer or AppKind.WasmApp) {
                var backendRef = DotNetObjectReference.Create<INotificationUIBackend>(this);
                await JS.InvokeVoidAsync(
                    $"{NotificationBlazorUIModule.ImportName}.NotificationUI.init",
                    backendRef,
                    HostInfo.AppKind.ToString()).ConfigureAwait(false);
            }
            else if (HostInfo.AppKind == AppKind.MauiApp) {
                // There should be no cycle reference as we implement INotificationPermissions for MAUI platform separately
                var notificationPermissions = services.GetRequiredService<INotificationPermissions>();
                var permissionState = await notificationPermissions.GetPermissionState(CancellationToken.None).ConfigureAwait(false);
                SetPermissionState(permissionState);
            }
            await _whenPermissionStateReady.Task.ConfigureAwait(false);
        }
    }

    public async ValueTask RegisterRequestNotificationHandler(ElementReference reference)
    {
        if (HostInfo.AppKind is AppKind.WebServer or AppKind.WasmApp)
            await JS.InvokeVoidAsync(
                $"{NotificationBlazorUIModule.ImportName}.NotificationUI.registerRequestNotificationHandler",
                reference
            ).ConfigureAwait(false);
    }

    public async Task<PermissionState> GetPermissionState(CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);
        return _permissionState.Value;
    }

    public Task RequestNotificationPermission(CancellationToken cancellationToken)
        // Web browser notification permission requests are handled at notification-ui.ts
        => Task.CompletedTask;

    [JSInvokable]
    public void HandleNotificationNavigation(string absoluteUrl)
    {
        // This method can be invoked from any synchronization context
        if (LocalUrl.FromAbsolute(absoluteUrl, UrlMapper) is not { } localUrl)
            return;
        if (!localUrl.IsChat())
            return;

        _ = AutoNavigationUI.DispatchNavigateTo(localUrl, AutoNavigationReason.Notification);
    }

    public void SetPermissionState(PermissionState state)
    {
        try {
            lock (Lock) {
                if (state == _permissionState.Value)
                    return;
            }
            _permissionState.Value = state;
            if (state == Blazor.PermissionState.Granted)
                RegisterDevice();
        }
        finally {
            _whenPermissionStateReady.TrySetResult();
        }
    }

    [JSInvokable]
    public void SetPermissionState(string permissionState)
    {
        var state = permissionState switch {
            "granted" => Blazor.PermissionState.Granted,
            "prompt" => Blazor.PermissionState.Prompt,
            _ => Blazor.PermissionState.Denied,
        };
        SetPermissionState(state);
    }

    // Private methods

    public void RegisterDevice()
    {
        if (_registerDeviceTask != null)
            return;
        lock (Lock) {
            if (_registerDeviceTask != null)
                return;

            _registerDeviceTask = Task.Run(async () => {
                string? deviceId = null;
                while (true) {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var cts = StopToken.LinkWith(timeoutCts.Token);
                    var cancellationToken = cts.Token;
                    try {
                        deviceId ??= await DeviceTokenRetriever.GetDeviceToken(cancellationToken).ConfigureAwait(false);
                        if (deviceId != null) {
                            var command = new Notifications_RegisterDevice(Session, deviceId, DeviceType.WebBrowser);
                            await Services.Commander().Run(command, cancellationToken).ConfigureAwait(false);
                        }
                        return deviceId;
                    }
                    catch (Exception e) when (!StopToken.IsCancellationRequested) {
                        Log.LogError(e, "Failed to register notification device - will retry");
                    }
                    await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                }
            }, CancellationToken.None);
        }
    }
}
