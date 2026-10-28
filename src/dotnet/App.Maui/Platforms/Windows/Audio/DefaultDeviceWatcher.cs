using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Role = NAudio.CoreAudioApi.Role;

namespace ActualChat.App.Maui.Audio;

/// <summary>
/// Reports a change of the default Multimedia endpoint for one data flow. A change back to
/// the opened device is ignored: the endpoint opened there is still the default.
/// </summary>
internal sealed class DefaultDeviceWatcher(
    DataFlow dataFlow,
    string? openedDeviceId,
    Action onChanged
    ) : IMMNotificationClient
{
    private DataFlow DataFlow { get; } = dataFlow;
    private string? OpenedDeviceId { get; } = openedDeviceId;
    private Action OnChanged { get; } = onChanged;

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        // WinRT ids embed the WASAPI endpoint id the notification carries
        if (flow != DataFlow || role != Role.Multimedia)
            return;

        if (OpenedDeviceId != null
            && !defaultDeviceId.IsNullOrEmpty()
            && OpenedDeviceId.Contains(defaultDeviceId, StringComparison.OrdinalIgnoreCase))
            return;

        OnChanged();
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string deviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string deviceId, PropertyKey propertyKey) { }
}
