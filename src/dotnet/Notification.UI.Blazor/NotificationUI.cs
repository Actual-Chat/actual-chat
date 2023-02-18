using System.Text.RegularExpressions;
using ActualChat.Hosting;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Notification.UI.Blazor;

public class NotificationUI : INotificationUIBackend
{
    private readonly object _lock = new();
    private readonly IMutableState<PermissionState> _state;
    private string? _deviceId;

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

    public NotificationUI(IServiceProvider services)
    {
        History = services.GetRequiredService<History>();
        DeviceTokenRetriever = services.GetRequiredService<IDeviceTokenRetriever>();
        UICommander = services.GetRequiredService<UICommander>();

        _state = services.StateFactory().NewMutable<PermissionState>();

        WhenInitialized = Initialize();

        async Task Initialize()
        {
            var backendRef = DotNetObjectReference.Create<INotificationUIBackend>(this);
            await JS.InvokeVoidAsync(
                $"{NotificationBlazorUIModule.ImportName}.NotificationUI.init",
                backendRef,
                HostInfo.AppKind.ToString());
        }
    }

    [ComputeMethod]
    public virtual async Task<string?> GetDeviceId()
    {
        await WhenInitialized;

        lock (_lock)
            return _deviceId;
    }

    [ComputeMethod]
    public virtual async Task<bool> IsDeviceRegistered()
    {
        await WhenInitialized;

        lock (_lock)
            return _deviceId != null;
    }

    public async Task EnsureDeviceRegistered(CancellationToken cancellationToken)
    {
        if (_deviceId != null)
            return;

        lock (_lock)
            if (_deviceId != null)
                return;

        var deviceId = await DeviceTokenRetriever.GetDeviceToken(cancellationToken);
        if (deviceId != null)
            _ = RegisterDevice(deviceId, cancellationToken);
    }

    public ValueTask RegisterRequestNotificationHandler(ElementReference reference)
        => JS.InvokeVoidAsync(
            $"{NotificationBlazorUIModule.ImportName}.NotificationUI.registerRequestNotificationHandler",
            reference
        );

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

        return Task.CompletedTask;
    }

    public bool IsAlreadyThere(ChatId chatId)
        => History.LocalUrl == Links.Chat(chatId);

    private async Task RegisterDevice(string deviceId, CancellationToken cancellationToken) {
        lock (_lock)
            _deviceId = deviceId;
        using (Computed.Invalidate()) {
            _ = GetDeviceId();
            _ = IsDeviceRegistered();
        }

        var command = new INotifications.RegisterDeviceCommand(Session, deviceId, DeviceType.WebBrowser);
        await UICommander.Run(command, cancellationToken);
    }
}
