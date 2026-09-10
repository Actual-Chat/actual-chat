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
        // A newly added DeviceType must be a deliberate decision here, or it silently
        // receives no pushes at all (IsFcm fails closed for anything unlisted).
        var fcmTypes = new HashSet<DeviceType> {
            DeviceType.WebBrowser,
            DeviceType.WindowsApp,
            DeviceType.iOSApp,
            DeviceType.AndroidApp,
        };

        // act
        var actual = Enum.GetValues<DeviceType>().ToDictionary(t => t, t => t.IsFcm());

        // assert
        actual.Should().BeEquivalentTo(
            Enum.GetValues<DeviceType>().ToDictionary(t => t, fcmTypes.Contains));
    }

    [Fact]
    public void AnUnknownDeviceTypeIsNotFcm()
    {
        // A type added later must default to "not FCM": handing a direct-push token to
        // FCM gets the device row deleted, so the predicate has to fail closed.
        // act + assert
        ((DeviceType)9999).IsFcm().Should().BeFalse();
    }
}
