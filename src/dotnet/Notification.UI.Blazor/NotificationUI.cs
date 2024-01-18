using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Notification.UI.Blazor.Module;
using ActualChat.UI.Blazor.Services;
using ActualLab.Rpc;

namespace ActualChat.Notification.UI.Blazor;

public class NotificationUI : ProcessorBase, INotificationUIBackend, INotificationsPermission
{
    private const int MaxRetryCount = 5;

    private static readonly string JSInitMethod = $"{NotificationBlazorUIModule.ImportName}.NotificationUI.init";
    private static readonly string JSRegisterRequestNotificationHandlerMethod =
        $"{NotificationBlazorUIModule.ImportName}.NotificationUI.registerRequestNotificationHandler";
    private static readonly string JSUnregisterRequestNotificationHandlerMethod =
        $"{NotificationBlazorUIModule.ImportName}.NotificationUI.unregisterRequestNotificationHandler";

    private readonly IMutableState<bool?> _permissionState;
    private readonly TaskCompletionSource _whenPermissionStateReady = TaskCompletionSourceExt.New();
    private volatile Task<string?>? _registerDeviceTask;
    private IDeviceTokenRetriever? _deviceTokenRetriever;
    private ILogger? _log;

    private ILogger Log => _log ??= Hub.LogFor(GetType());

    private UIHub Hub { get; }
    private HostInfo HostInfo => Hub.HostInfo();
    private Session Session => Hub.Session();
    private History History => Hub.History;
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
    public Task HandleNotificationNavigation(string absoluteUrl)
    {
        // This method can be invoked from any synchronization context
        if (LocalUrl.FromAbsolute(absoluteUrl, UrlMapper) is not { } localUrl)
            return Task.CompletedTask;
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

    // Private methods

    private void RegisterDevice()
    {
        if (_registerDeviceTask != null)
            return;
        lock (Lock) {
            if (_registerDeviceTask != null)
                return;

            _registerDeviceTask = Task.Run(async () => {
                string? deviceId = null;
                for (int i = 0; i < MaxRetryCount; i++) {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var cts = StopToken.LinkWith(timeoutCts.Token);
                    var cancellationToken = cts.Token;
                    try {
                        deviceId ??= await DeviceTokenRetriever.GetDeviceToken(cancellationToken).ConfigureAwait(false);
                        await Hub.RpcHub().WhenClientPeerConnected(cancellationToken).ConfigureAwait(false);
                        if (deviceId != null) {
                            var command = new Notifications_RegisterDevice(Session, deviceId, DeviceType.WebBrowser);
                            await Hub.Commander().Call(command, cancellationToken).ConfigureAwait(false);
                        }
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
    }
}
