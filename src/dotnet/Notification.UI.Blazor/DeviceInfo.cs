using ActualChat.UI.Blazor.Module;

namespace ActualChat.Notification.UI.Blazor;

public class DeviceInfo
{
    private readonly object _lock = new();
    private readonly IJSRuntime _js;
    private readonly Session _session;
    private readonly UICommander _uiCommander;

    private string? _deviceId;

    public DeviceInfo(Session session, IJSRuntime js, UICommander uiCommander)
    {
        _session = session;
        _js = js;
        _uiCommander = uiCommander;
    }

    [ComputeMethod]
    public virtual Task<string?> GetDeviceId()
    {
        lock (_lock)
            return Task.FromResult(_deviceId);
    }

    [ComputeMethod]
    public virtual Task<bool> IsDeviceRegistered()
    {
        lock (_lock)
            return Task.FromResult(_deviceId != null);
    }

    public async Task EnsureDeviceRegistered(CancellationToken cancellationToken)
    {
        if (_deviceId != null)
            return;

        lock (_lock)
            if (_deviceId != null)
                return;

        var deviceId = await _js.InvokeAsync<string?>($"{BlazorUICoreModule.ImportName}.getDeviceToken", cancellationToken);
        if (deviceId != null)
            _ = RegisterDevice(deviceId, cancellationToken);
    }

    private async Task RegisterDevice(string deviceId, CancellationToken cancellationToken) {
        lock (_lock)
            _deviceId = deviceId;
        using (Computed.Invalidate()) {
            _ = GetDeviceId();
            _ = IsDeviceRegistered();
        }

        var command = new INotifications.RegisterDeviceCommand(_session, deviceId, DeviceType.WebBrowser);
        await _uiCommander.Run(command, cancellationToken);
    }
}
