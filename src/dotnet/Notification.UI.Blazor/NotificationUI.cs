using ActualChat.Hosting;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Notification.UI.Blazor;

public class NotificationUI : INotificationUIBackend, INotificationPermissions
{
    private readonly object _lock = new();
    private readonly IMutableState<PermissionState> _state;
    private readonly IMutableState<string?> _deviceId;
    private readonly TaskCompletionSource<Unit> _whenReadySource = TaskCompletionSourceExt.New<Unit>();

    private IDeviceTokenRetriever DeviceTokenRetriever { get; }
    private History History { get; }
    private Session Session => History.Session;
    private HostInfo HostInfo => History.HostInfo;
    private UrlMapper UrlMapper => History.UrlMapper;
    private Dispatcher Dispatcher => History.Dispatcher;
    private IJSRuntime JS => History.JS;
    private PanelsUI PanelsUI => History.Services.GetRequiredService<PanelsUI>();
    private UICommander UICommander { get; }
    private ILogger Log { get; }

    public Task WhenInitialized { get; }
    public IState<PermissionState> State => _state;
    public IState<string?> DeviceId => _deviceId;

    public NotificationUI(IServiceProvider services)
    {
        History = services.GetRequiredService<History>();
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

    public void DispatchNotificationNavigation(string url)
        // Called from MainActivity, i.e. unclear if it's running in Blazor Dispatcher
        => _ = Dispatcher.InvokeAsync(async () => {
            try {
                await HandleNotificationNavigation(url);
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to dispatch notification navigation");
            }
        });

    [JSInvokable]
    public async Task HandleNotificationNavigation(string url)
    {
        var relativeUrl = LocalUrl.FromAbsolute(url, UrlMapper);
        if (relativeUrl?.IsChatId() == true) {
            await History.NavigateTo(relativeUrl);
            PanelsUI.Middle.EnsureVisible();
        }
    }

    public void UpdateNotificationStatus(PermissionState newState)
    {
        if (newState != _state.Value)
            _state.Value = newState;
        if (newState == PermissionState.Granted)
            _ = EnsureDeviceRegistered(CancellationToken.None);

        _whenReadySource.SetResult(Unit.Default);
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

        _whenReadySource.SetResult(Unit.Default);
    }

    public bool IsAlreadyThere(ChatId chatId)
        => History.LocalUrl == Links.Chat(chatId);

    private async Task RegisterDevice(string deviceId, CancellationToken cancellationToken) {
        lock (_lock)
            _deviceId.Value = deviceId;

        var command = new INotifications.RegisterDeviceCommand(Session, deviceId, DeviceType.WebBrowser);
        await UICommander.Run(command, cancellationToken).ConfigureAwait(false);
    }
}
