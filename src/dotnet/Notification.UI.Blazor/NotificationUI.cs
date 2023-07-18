using ActualChat.Hosting;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Notification.UI.Blazor;

public class NotificationUI : INotificationUIBackend, INotificationPermissions
{
    private readonly object _lock = new();
    private readonly IMutableState<PermissionState> _state;
    private readonly IMutableState<string?> _deviceId;
    private readonly TaskCompletionSource _whenStateSet = TaskCompletionSourceExt.New();
    private History? _history;
    private AutoNavigationUI? _autoNavigationUI;
    private IDeviceTokenRetriever? _deviceTokenRetriever;
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private History History => _history ??= Services.GetRequiredService<History>();
    private AutoNavigationUI AutoNavigationUI => _autoNavigationUI ??= Services.GetRequiredService<AutoNavigationUI>();
    private IDeviceTokenRetriever DeviceTokenRetriever => _deviceTokenRetriever ??= Services.GetRequiredService<IDeviceTokenRetriever>();
    private Session Session => History.Session;
    private HostInfo HostInfo => History.HostInfo;
    private UrlMapper UrlMapper => History.UrlMapper;
    private IJSRuntime JS => History.JS;
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public IState<PermissionState> State => _state;
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<string?> DeviceId => _deviceId;
    public Task WhenReady { get; }

    public NotificationUI(IServiceProvider services)
    {
        Services = services;

        var stateFactory = services.StateFactory();
        _state = stateFactory.NewMutable(PermissionState.Denied, nameof(State));
        _deviceId = stateFactory.NewMutable(default(string?), nameof(DeviceId));
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
                var permissionState = await notificationPermissions.GetNotificationPermissionState(CancellationToken.None).ConfigureAwait(false);
                UpdateNotificationStatus(permissionState);
            }
            await _whenStateSet.Task.ConfigureAwait(false);
        }
    }

    public async Task EnsureDeviceRegistered(CancellationToken cancellationToken)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        if (_deviceId.Value != null)
            return;
        lock (_lock)
            if (_deviceId.Value != null)
                return;

        var deviceId = await DeviceTokenRetriever.GetDeviceToken(cancellationToken).ConfigureAwait(false);
        if (deviceId != null)
            await RegisterDevice(deviceId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RegisterRequestNotificationHandler(ElementReference reference)
    {
        if (HostInfo.AppKind is AppKind.WebServer or AppKind.WasmApp)
            await JS.InvokeVoidAsync(
                $"{NotificationBlazorUIModule.ImportName}.NotificationUI.registerRequestNotificationHandler",
                reference
            ).ConfigureAwait(false);
    }

    public async Task<PermissionState> GetNotificationPermissionState(CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);
        return _state.Value;
    }

    public Task RequestNotificationPermissions(CancellationToken cancellationToken)
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

    public void UpdateNotificationStatus(PermissionState newState)
    {
        if (newState != _state.Value)
            _state.Value = newState;
        if (newState == PermissionState.Granted)
            _ = EnsureDeviceRegistered(CancellationToken.None);
        _whenStateSet.SetResult();
    }

    [JSInvokable]
    public async Task UpdateNotificationStatus(string permissionState)
    {
        var newState = permissionState switch {
            "granted" => PermissionState.Granted,
            "prompt" => PermissionState.Prompt,
            _ => PermissionState.Denied,
        };
        if (newState != _state.Value)
            _state.Value = newState;
        if (newState == PermissionState.Granted)
            await EnsureDeviceRegistered(CancellationToken.None).ConfigureAwait(false);

        _whenStateSet.SetResult();
    }

    private async Task RegisterDevice(string deviceId, CancellationToken cancellationToken) {
        lock (_lock)
            _deviceId.Value = deviceId;

        var command = new Notifications_RegisterDevice(Session, deviceId, DeviceType.WebBrowser);
        await Services.Commander().Run(command, cancellationToken).ConfigureAwait(false);
    }
}
