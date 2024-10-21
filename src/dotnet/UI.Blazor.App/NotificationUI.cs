using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Notification;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App;

public class NotificationUI : ProcessorBase, INotificationUI, INotificationUIBackend, INotificationsPermission
{
    private const int MaxRetryCount = 5;

    private static readonly string JSInitMethod = $"{BlazorUIAppModule.ImportName}.NotificationUI.init";
    private static readonly string JSRegisterRequestNotificationHandlerMethod =
        $"{BlazorUIAppModule.ImportName}.NotificationUI.registerRequestNotificationHandler";
    private static readonly string JSUnregisterRequestNotificationHandlerMethod =
        $"{BlazorUIAppModule.ImportName}.NotificationUI.unregisterRequestNotificationHandler";

    private readonly MutableState<bool?> _permissionState;
    private readonly TaskCompletionSource _whenPermissionStateReady = TaskCompletionSourceExt.New();
    private volatile Task<string?>? _registerDeviceTask;
    private IDeviceTokenRetriever? _deviceTokenRetriever;
    private ILogger? _log;

    private ILogger Log => _log ??= Hub.LogFor(GetType());

    private UIHub Hub { get; }
    private HostInfo HostInfo => Hub.HostInfo();
    private Session Session => Hub.Session();
    private AutoNavigationUI AutoNavigationUI => Hub.AutoNavigationUI;
    private IDeviceTokenRetriever DeviceTokenRetriever => _deviceTokenRetriever ??= Hub.GetRequiredService<IDeviceTokenRetriever>();
    private UrlMapper UrlMapper => Hub.UrlMapper();
    private IJSRuntime JS => Hub.JSRuntime();

    public IState<bool?> PermissionState => _permissionState;
    public Task WhenReady { get; }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NotificationUI))]
    public NotificationUI(UIHub hub)
    {
        Hub = hub;

        var stateFactory = hub.StateFactory();
        _permissionState = stateFactory.NewMutable((bool?)null, nameof(PermissionState));
        WhenReady = Initialize();

        async Task Initialize() {
            if (HostInfo.HostKind is HostKind.Server or HostKind.WasmApp) {
                var backendRef = DotNetObjectReference.Create<INotificationUIBackend>(this);
                await JS.InvokeVoidAsync(JSInitMethod, backendRef, HostInfo.HostKind.ToString()).ConfigureAwait(false);
            }
            else if (HostInfo.HostKind == HostKind.MauiApp) {
                // There should be no cycle reference as we implement INotificationPermissions for MAUI platform separately
                var notificationsPermission = hub.GetRequiredService<INotificationsPermission>();
                var isGranted = await notificationsPermission.IsGranted().ConfigureAwait(false);
                SetIsGranted(isGranted);
            }
            await _whenPermissionStateReady.Task.ConfigureAwait(false);
        }
    }

    public async ValueTask RegisterRequestNotificationHandler(ElementReference reference)
    {
        if (HostInfo.HostKind is HostKind.Server or HostKind.WasmApp)
            await JS.InvokeVoidAsync(JSRegisterRequestNotificationHandlerMethod, reference).ConfigureAwait(false);
    }

    public async ValueTask UnregisterRequestNotificationHandler(ElementReference reference)
    {
        if (HostInfo.HostKind is HostKind.Server or HostKind.WasmApp)
            await JS.InvokeVoidAsync(JSUnregisterRequestNotificationHandlerMethod, reference).ConfigureAwait(false);
    }

    public async Task<bool?> IsGranted(CancellationToken cancellationToken = default)
    {
        await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        return _permissionState.Value;
    }

    public Task Request(CancellationToken cancellationToken = default)
        => Task.CompletedTask; // Actually handled by notification-ui.ts

    [JSInvokable]
    public Task NavigateToNotificationUrl(string url)
    {
        Log.LogInformation("NavigateToNotificationUrl, Url: {Url}", url);

        // This method can be invoked from any synchronization context
        Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri);
        if (uri == null)
            return Task.CompletedTask;

        LocalUrl localUrl;
        if (uri.IsAbsoluteUri) {
            var tempLocalUrl = LocalUrl.FromAbsolute(url, UrlMapper);
            if (tempLocalUrl is null)
                return Task.CompletedTask;

            localUrl = tempLocalUrl.Value;
        }
        else
            localUrl = new LocalUrl(url, ParseOrNone.Option);

        if (!localUrl.IsChat())
            return Task.CompletedTask;

        return AutoNavigationUI.DispatchNavigateTo(localUrl, AutoNavigationReason.Notification);
    }

    public void SetIsGranted(bool? isGranted)
    {
        try {
            lock (Lock) {
                if (isGranted == _permissionState.Value)
                    return;
            }
            // Log.LogWarning("SetPermissionState: {Value} @ #{Hash}", permissionState, GetHashCode());
            _permissionState.Value = isGranted;
            if (isGranted == true)
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
            "granted" => true,
            "prompt" => (bool?)null,
            _ => false,
        };
        SetIsGranted(state);
    }

    public async Task DeregisterDevice(CancellationToken cancellationToken = default)
    {
        Log.LogInformation("-> DeregisterDevice");
        var deviceId = await DeviceTokenRetriever.GetDeviceToken(cancellationToken).ConfigureAwait(false);
        if (deviceId == null)
            return;

        Log.LogInformation("DeregisterDevice. About to execute DeleteDeviceToken");
        var deleteTokenTask = DeviceTokenRetriever.DeleteDeviceToken(cancellationToken);
        Log.LogInformation("DeregisterDevice. About to execute DeregisterDevice command");
        var command = new Notifications_DeregisterDevice(Session, deviceId);
        var deregisterDeviceTask =  Hub.Commander().Call(command, cancellationToken);
        await Task.WhenAll(deleteTokenTask, deregisterDeviceTask).ConfigureAwait(false);
        Log.LogInformation("DeregisterDevice. DeleteDeviceToken and DeregisterDevice command are executed");
    }

    public async Task EnsureDeviceRegistered(CancellationToken cancellationToken = default)
    {
        Log.LogInformation("-> EnsureDeviceRegistered");
        var deviceId = await DeviceTokenRetriever.GetDeviceToken(cancellationToken).ConfigureAwait(false);
        Log.LogInformation("EnsureDeviceRegistered. Got device token");
        var existingTask = _registerDeviceTask;
        if (existingTask != null) {
            Log.LogInformation("EnsureDeviceRegistered. RegisterDeviceTask exists");
            var alreadyRegisteredDeviceId = await existingTask.ConfigureAwait(false);
            Log.LogInformation("EnsureDeviceRegistered. RegisterDeviceTask has completed");
            if (OrdinalEquals(alreadyRegisteredDeviceId, deviceId)) {
                Log.LogInformation("EnsureDeviceRegistered. Device token is already registered");
                return;
            }
        }
        lock (Lock) {
            Log.LogInformation("EnsureDeviceRegistered. Registered device token does not match. Will try again");
            _registerDeviceTask = null;
            RegisterDevice(deviceId, cancellationToken);
            existingTask = _registerDeviceTask;
        }
        await existingTask!.ConfigureAwait(false);
    }

    // Private methods

    private void RegisterDevice(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        if (_registerDeviceTask != null)
            return;
        lock (Lock) {
            if (_registerDeviceTask != null)
                return;

            var parentToken = cancellationToken == default
                ? StopToken
                : cancellationToken;
            _registerDeviceTask = Task.Run(async () => {
                // Wait for sign-in
                Log.LogInformation("-> RegisterDeviceTask has started. Waiting for loading account...");
                await Hub.AccountUI.WhenLoaded.ConfigureAwait(false);
                for (int i = 0; i < MaxRetryCount; i++) {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var cts = parentToken.LinkWith(timeoutCts.Token);
                    var linkedToken = cts.Token;
                    try {
                        Log.LogInformation("RegisterDeviceTask. Attempt: {Attempt}/{MaxRetryCount}", i + 1, MaxRetryCount);
                        await Hub.RpcHub().WhenClientPeerConnected(linkedToken).ConfigureAwait(false);
                        Log.LogInformation("RegisterDeviceTask. Peer has got connected");
                        deviceId ??= await DeviceTokenRetriever.GetDeviceToken(linkedToken).ConfigureAwait(false);
                        if (deviceId == null) {
                            Log.LogError("Failed to get notification device token");
                            return deviceId;
                        }

                        Log.LogInformation("RegisterDeviceTask. Retrieved device token");
                        var isGuest = Hub.AccountUI.OwnAccount.Value.IsGuestOrNone;
                        if (isGuest) {
                            Log.LogInformation("RegisterDeviceTask. Awaiting user is signed in");
                            await Hub.AccountUI.OwnAccount.When(acc => !acc.IsGuestOrNone, cts.Token)
                                .ConfigureAwait(false);
                        }

                        if (Log.IsEnabled(LogLevel.Trace))
                            Log.LogInformation("RegisterDeviceTask. About to send register command. UserId is {UserId}", Hub.AccountUI.OwnAccount.Value.Id);
                        else
                            Log.LogInformation("RegisterDeviceTask. About to send register command");
                        var command = new Notifications_RegisterDevice(Session, deviceId, GetDeviceType());
                        await Hub.Commander().Call(command, linkedToken).ConfigureAwait(false);
                        Log.LogInformation("RegisterDeviceTask. Register command has been executed");
                        return deviceId;
                    }
                    catch (Exception e) when (!StopToken.IsCancellationRequested) {
                        Log.LogError(e, "Failed to register notification device - will retry");
                    }
                    await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                }
                return null;
            }, CancellationToken.None);
        }

        DeviceType GetDeviceType()
        {
            if (HostInfo.HostKind.IsMauiApp())
                switch (HostInfo.AppKind) {
                case AppKind.Android:
                    return DeviceType.AndroidApp;
                case AppKind.Ios:
                    return DeviceType.iOSApp;
                case AppKind.Windows:
                    return DeviceType.WindowsApp;
                }

            return DeviceType.WebBrowser;
        }
    }
}
