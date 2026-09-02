namespace ActualChat.Notifications.IntegrationTests;

public sealed class DeviceTypeExtTest
{
    [Theory]
    [InlineData(DeviceType.WebBrowser)]
    [InlineData(DeviceType.WindowsApp)]
    [InlineData(DeviceType.iOSApp)]
    [InlineData(DeviceType.AndroidApp)]
    public void FcmDeliverableTypesAreFcm(DeviceType deviceType)
        => deviceType.IsFcm().Should().BeTrue();

    [Theory]
    [InlineData(DeviceType.iOSPttApp)]
    [InlineData(DeviceType.iOSVoipApp)]
    public void DirectApnsTypesAreNotFcm(DeviceType deviceType)
        => deviceType.IsFcm().Should().BeFalse();

    [Fact]
    public void EveryDeviceTypeIsClassified()
    {
        // A new device type must be a deliberate decision on both sides, not a default.
        foreach (var deviceType in Enum.GetValues<DeviceType>())
            deviceType.IsFcm().Should().Be(deviceType is not (DeviceType.iOSPttApp or DeviceType.iOSVoipApp));
    }
}
