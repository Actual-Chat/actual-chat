using ActualChat.UI.Blazor.Module;

namespace ActualChat.Notification.UI.Blazor;

public class DeviceInfo
{
    private readonly object _lock = new();
    private readonly IJSRuntime _js;
    private readonly Session _session;
    private readonly UICommandRunner _cmd;

    private string? _deviceId;

    public DeviceInfo(IJSRuntime js, Session session, UICommandRunner cmd)
    {
        _js = js;
        _session = session;
        _cmd = cmd;
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

        var deviceId = await _js.InvokeAsync<string?>($"{BlazorUICoreModule.ImportName}.getDeviceToken", cancellationToken)
            .ConfigureAwait(true);
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
        await _cmd.Run(command, cancellationToken).ConfigureAwait(true);
    }
}
