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
    public void FcmSetIsExplicitlyListed()
    {
        var fcmTypes = new[] {
            DeviceType.WebBrowser,
            DeviceType.WindowsApp,
            DeviceType.iOSApp,
            DeviceType.AndroidApp,
        };
        foreach (var fcmType in fcmTypes)
            fcmType.IsFcm().Should().BeTrue();
    }

    [Fact]
    public void AnUnknownDeviceTypeIsNotFcm()
    {
        // A type added later must default to "not FCM": handing a direct-push token to
        // FCM gets the device row deleted, so the predicate has to fail closed.
        ((DeviceType)9999).IsFcm().Should().BeFalse();
    }
}
