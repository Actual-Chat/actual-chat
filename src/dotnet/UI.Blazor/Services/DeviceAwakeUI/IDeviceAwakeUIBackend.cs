namespace ActualChat.UI.Blazor.Services;

public interface IDeviceAwakeUIBackend
{
    void OnDeviceAwake(double totalSleepDurationMs);
}
