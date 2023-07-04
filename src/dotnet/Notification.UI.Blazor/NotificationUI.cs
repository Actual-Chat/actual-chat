using ActualChat.Hosting;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Notification.UI.Blazor;

public class NotificationUI : INotificationUIBackend, INotificationPermissions
{
    private readonly object _lock = new();
    private readonly IMutableState<PermissionState> _state;
    private readonly IMutableState<string?> _deviceId;
    private readonly TaskCompletionSource _whenReadySource = TaskCompletionSourceExt.New();

    private IDeviceTokenRetriever DeviceTokenRetriever { get; }
    private History History { get; }
    private AutoNavigationUI AutoNavigationUI { get; }
    private Session Session => History.Session;
    private HostInfo HostInfo => History.HostInfo;
    private UrlMapper UrlMapper => History.UrlMapper;
    private Dispatcher Dispatcher => History.Dispatcher;
    private IJSRuntime JS => History.JS;
    private UICommander UICommander { get; }
    private ILogger Log { get; }

    public Task WhenInitialized { get; }
    public IState<PermissionState> State => _state;
    // ReSharper disable once InconsistentlySynchronizedField
    public IState<string?> DeviceId => _deviceId;

    public NotificationUI(IServiceProvider services)
    {
        History = services.GetRequiredService<History>();
        AutoNavigationUI = services.GetRequiredService<AutoNavigationUI>();
        DeviceTokenRetriever = services.GetRequiredService<IDeviceTokenRetriever>();
        UICommander = services.GetRequiredService<UICommander>();
        Log = services.LogFor(GetType());

        var stateFactory = services.StateFactory();
        _state = stateFactory.NewMutable(default(PermissionState), nameof(State));
        _deviceId = stateFactory.NewMutable(default(string?), nameof(DeviceId));

        WhenInitialized = Initialize();

        async Task Initialize()
        {
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

            await _whenReadySource.Task.ConfigureAwait(false);
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
        await WhenInitialized.ConfigureAwait(false);
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

        _whenReadySource.SetResult();
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

        _whenReadySource.SetResult();
    }

    public bool IsAlreadyThere(ChatId chatId)
        => History.LocalUrl == Links.Chat(chatId);

    private async Task RegisterDevice(string deviceId, CancellationToken cancellationToken) {
        lock (_lock)
            _deviceId.Value = deviceId;

        var command = new Notifications_RegisterDevice(Session, deviceId, DeviceType.WebBrowser);
        await UICommander.Run(command, cancellationToken).ConfigureAwait(false);
    }
}
