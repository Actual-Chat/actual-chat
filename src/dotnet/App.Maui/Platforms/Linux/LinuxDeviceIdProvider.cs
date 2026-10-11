using ActualChat.Hashing;
using banditoth.MAUI.DeviceId.Interfaces;

namespace ActualChat.App.Maui;

/// <summary>
/// <see cref="IDeviceIdProvider"/> for the GTK backend, where banditoth.MAUI.DeviceId has no
/// build: the device id is a hash of the systemd machine id, so it survives reinstalls and
/// preference wipes; the installation id is the per-install fallback for systems without one.
/// </summary>
public sealed class LinuxDeviceIdProvider : IDeviceIdProvider
{
    private static readonly LazySlim<string?> MachineDeviceIdLazy = new(GetMachineDeviceId);
    private static readonly ILogger Log = StaticLog.For<LinuxDeviceIdProvider>();

    public string GetDeviceId()
        => MachineDeviceIdLazy.Value ?? GetInstallationId();

    public string GetInstallationId()
        => MauiPreferences.InstallationId;

    // Private methods

    private static string? GetMachineDeviceId()
    {
        string machineId;
        try {
            machineId = File.ReadAllText("/etc/machine-id").Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            machineId = "";
        }
        if (machineId.IsNullOrEmpty()) {
            Log.LogWarning("No /etc/machine-id - falling back to the installation id");
            return null;
        }

        // machine-id(5) asks to keep the raw id private, so only a salted hash leaves the process
        var deviceId = $"voxt-linux:{machineId}".Hash().SHA256().AlphaNumeric();
        Log.LogInformation("Device id from the machine id: {DeviceId}", deviceId);
        return deviceId;
    }
}
