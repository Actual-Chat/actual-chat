using ActualChat.Notifications.Db;

namespace ActualChat.Notifications.IntegrationTests;

public sealed class DeviceModelTest
{
    [Fact]
    public void ToModelShouldCarrySessionHash()
    {
        // arrange
        var dbDevice = new DbDevice {
            Id = "token-1",
            Type = DeviceType.iOSVoipApp,
            SessionHash = "session-hash-1",
            CreatedAt = DateTime.UtcNow,
        };

        // act
        var device = dbDevice.ToModel();

        // assert
        device.SessionHash.Value.Should().Be("session-hash-1");
        device.DeviceType.Should().Be(DeviceType.iOSVoipApp);
    }

    [Fact]
    public void SessionHashShouldDefaultToEmpty()
        => new Device("token-1", DeviceType.iOSApp, Moment.EpochStart)
            .SessionHash.Should().Be(Symbol.Empty);
}
