namespace ActualChat.Notifications.IntegrationTests;

public class CallRingFanOutTest
{
    [Fact]
    public void VoipDevicesShouldGetTheRing()
    {
        // arrange
        var devices = new[] {
            NewDevice("fcm-1", DeviceType.iOSApp, "phone-a"),
            NewDevice("voip-1", DeviceType.iOSVoipApp, "phone-a"),
        };

        // act
        var voip = NotificationsBackend.SelectVoipCallDevices(devices);

        // assert
        voip.Select(d => d.DeviceId.Value).Should().Equal("voip-1");
    }

    [Fact]
    public void AnFcmDeviceShouldBeSuppressedWhenItsOwnPhoneHasAVoipToken()
    {
        // arrange
        var devices = new[] {
            NewDevice("fcm-1", DeviceType.iOSApp, "phone-a"),
            NewDevice("voip-1", DeviceType.iOSVoipApp, "phone-a"),
        };

        // act
        var fcm = NotificationsBackend.SelectFcmCallDevices(devices, Delivered("voip-1"));

        // assert
        fcm.Should().BeEmpty();
    }

    [Fact]
    public void AnotherPhonesFcmDeviceShouldStillGetTheBanner()
    {
        // arrange
        var devices = new[] {
            NewDevice("fcm-a", DeviceType.iOSApp, "phone-a"),
            NewDevice("voip-a", DeviceType.iOSVoipApp, "phone-a"),
            NewDevice("fcm-b", DeviceType.iOSApp, "phone-b"),
            NewDevice("android", DeviceType.AndroidApp, "phone-c"),
        };

        // act
        var fcm = NotificationsBackend.SelectFcmCallDevices(devices, Delivered("voip-a"));

        // assert
        fcm.Select(d => d.DeviceId.Value).Should().BeEquivalentTo("fcm-b", "android");
    }

    [Fact]
    public void AnEmptySessionHashShouldNeverSuppressAnything()
    {
        // Legacy rows predate SessionHash; an empty hash must not read as "same phone"
        // and silence every other device.
        var devices = new[] {
            NewDevice("fcm-1", DeviceType.iOSApp, ""),
            NewDevice("fcm-2", DeviceType.iOSApp, ""),
            NewDevice("voip-1", DeviceType.iOSVoipApp, ""),
        };

        // act
        var fcm = NotificationsBackend.SelectFcmCallDevices(devices, Delivered("voip-1"));

        // assert
        fcm.Select(d => d.DeviceId.Value).Should().BeEquivalentTo("fcm-1", "fcm-2");
    }

    [Fact]
    public void NothingShouldBeSuppressedWhenTheRingReachedNoDevice()
    {
        // An unconfigured, failed or rejected ring delivers nothing, so the banner must stay.
        // arrange
        var devices = new[] {
            NewDevice("fcm-1", DeviceType.iOSApp, "phone-a"),
            NewDevice("voip-1", DeviceType.iOSVoipApp, "phone-a"),
        };

        // act
        var fcm = NotificationsBackend.SelectFcmCallDevices(devices, Delivered());

        // assert
        fcm.Select(d => d.DeviceId.Value).Should().BeEquivalentTo("fcm-1");
    }

    [Fact]
    public void PttDevicesShouldNeverBeRungOrBannered()
    {
        // arrange
        var devices = new[] { NewDevice("ptt-1", DeviceType.iOSPttApp, "phone-a") };

        // act + assert
        NotificationsBackend.SelectVoipCallDevices(devices).Should().BeEmpty();
        NotificationsBackend.SelectFcmCallDevices(devices, Delivered("ptt-1")).Should().BeEmpty();
    }

    // Private methods

    private static Device NewDevice(string deviceId, DeviceType deviceType, string sessionHash)
        => new (deviceId, deviceType, Moment.EpochStart) { SessionHash = sessionHash };

    private static IReadOnlySet<Symbol> Delivered(params string[] deviceIds)
        => deviceIds.Select(x => new Symbol(x)).ToHashSet();
}
