using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class CallRingPushTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(10);

    private IWebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);

    [Fact]
    public async Task ReRegisteringADeviceRefreshesItsSessionHash()
    {
        // arrange
        var alice = await Tester.SignInAsAlice();
        var deviceId = new Symbol($"call-device-rehash-{alice.Id.Value}");
        var backend = AppHost.Services.GetRequiredService<INotificationsBackend>();

        // act
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            alice.Id, deviceId, DeviceType.iOSVoipApp, "session-1"));
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            alice.Id, deviceId, DeviceType.iOSVoipApp, "session-2"));

        // assert
        var sessionHash = await WaitForSessionHash(backend, alice.Id, deviceId, "session-2");
        sessionHash.Should().Be("session-2", "a re-signed-in device must carry its new session hash");
    }

    // Private methods

    private static async Task<string> WaitForSessionHash(
        INotificationsBackend backend, UserId userId, Symbol deviceId, string expected)
    {
        var deadline = CpuTimestamp.Now + RingTimeout;
        var sessionHash = "";
        while (CpuTimestamp.Now < deadline) {
            var devices = await backend.ListDevices(userId, CancellationToken.None);
            sessionHash = devices.FirstOrDefault(d => d.DeviceId == deviceId)?.SessionHash.Value ?? "";
            if (sessionHash == expected)
                break;

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        return sessionHash;
    }
}
