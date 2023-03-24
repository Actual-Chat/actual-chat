using System.Text.RegularExpressions;
using ActualChat.Hosting;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Notification.UI.Blazor;

public class NotificationUI : INotificationUIBackend, INotificationPermissions
{
    private readonly object _lock = new();
    private readonly IMutableState<PermissionState> _state;
    private readonly IMutableState<string?> _deviceId;
    private readonly TaskSource<Unit> _whenReady;

    private IDeviceTokenRetriever DeviceTokenRetriever { get; }
    private History History { get; }
    private Session Session => History.Session;
    private HostInfo HostInfo => History.HostInfo;
    private UrlMapper UrlMapper => History.UrlMapper;
    private Dispatcher Dispatcher => History.Dispatcher;
    private IJSRuntime JS => History.JS;
    private UICommander UICommander { get; }

    public Task WhenInitialized { get; }
    public IState<PermissionState> State => _state;
    public IState<string?> DeviceId => _deviceId;

    public NotificationUI(IServiceProvider services)
    {
        History = services.GetRequiredService<History>();
        DeviceTokenRetriever = services.GetRequiredService<IDeviceTokenRetriever>();
        UICommander = services.GetRequiredService<UICommander>();

        var stateFactory = services.StateFactory();
        _state = stateFactory.NewMutable(default(PermissionState), nameof(State));
        _deviceId = stateFactory.NewMutable(default(string?), nameof(DeviceId));
        _whenReady = TaskSource.New<Unit>(true);

        WhenInitialized = Initialize();

        async Task Initialize()
        {
            if (HostInfo.AppKind is AppKind.WebServer or AppKind.WasmApp) {
                var backendRef = DotNetObjectReference.Create<INotificationUIBackend>(this);
                await JS.InvokeVoidAsync(
                    $"{NotificationBlazorUIModule.ImportName}.NotificationUI.init",
                    backendRef,
                    HostInfo.AppKind.ToString());
            }
            else if (HostInfo.AppKind == AppKind.MauiApp) {
                // There should be no cycle reference as we implement INotificationPermissions for MAUI platform separately
                var notificationPermissions = services.GetRequiredService<INotificationPermissions>();
                var permissionState = await notificationPermissions.GetNotificationPermissionState(CancellationToken.None);
                UpdateNotificationStatus(permissionState);
            }

            await _whenReady.Task.ConfigureAwait(false);
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

        var deviceId = await DeviceTokenRetriever.GetDeviceToken(cancellationToken);
        if (deviceId != null)
            _ = RegisterDevice(deviceId, cancellationToken);
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
    public Task HandleNotificationNavigation(string url)
    {
        // Called from MainActivity, i.e. unclear if it's running in Blazor Dispatcher
        Dispatcher.InvokeAsync(() => {
            var origin = UrlMapper.BaseUrl.TrimEnd('/');
            if (url.IsNullOrEmpty() || !url.OrdinalStartsWith(origin))
                return;

            var chatPageRe = new Regex($"^{Regex.Escape(origin)}/chat/(?<chatid>[a-z0-9-]+)(?:#(?<entryid>)\\d+)?");
            var match = chatPageRe.Match(url);
            if (!match.Success)
                return;

            // Take relative URL to eliminate difference between web app and MAUI app
            var relativeUrl = url[origin.Length..];

            var chatIdGroup = match.Groups["chatid"];
            if (chatIdGroup.Success)
                History.NavigateTo(relativeUrl);
        });
        return Task.CompletedTask;
    }

    public void UpdateNotificationStatus(PermissionState newState)
    {
        if (newState != _state.Value)
            _state.Value = newState;
        if (newState == PermissionState.Granted)
            _ = EnsureDeviceRegistered(CancellationToken.None);

        _whenReady.SetResult(Unit.Default);
    }

    [JSInvokable]
    public Task UpdateNotificationStatus(string permissionState)
    {
        var newState = permissionState switch {
            "granted" => PermissionState.Granted,
            "prompt" => PermissionState.Prompt,
            _ => PermissionState.Denied,
        };
        if (newState != _state.Value)
            _state.Value = newState;
        if (newState == PermissionState.Granted)
            _ = EnsureDeviceRegistered(CancellationToken.None);

        _whenReady.SetResult(Unit.Default);
        return Task.CompletedTask;
    }

    public bool IsAlreadyThere(ChatId chatId)
        => History.LocalUrl == Links.Chat(chatId);

    private async Task RegisterDevice(string deviceId, CancellationToken cancellationToken) {
        lock (_lock)
            _deviceId.Value = deviceId;

        var command = new INotifications.RegisterDeviceCommand(Session, deviceId, DeviceType.WebBrowser);
        await UICommander.Run(command, cancellationToken);
    }
}
