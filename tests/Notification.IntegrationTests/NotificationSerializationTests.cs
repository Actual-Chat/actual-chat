namespace ActualChat.Notification.IntegrationTests;

public class NotificationSerializationTests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void DeviceSerializationTest()
    {
        var d = new Device("1", DeviceType.AndroidApp, Moment.Now) { AccessedAt = Moment.Now };
        d.AssertPassesThroughAllSerializers();

        d = new Device("1", DeviceType.AndroidApp, Moment.Now);
        d.AssertPassesThroughAllSerializers();
    }

    [Fact]
    public void NotificationSerializationTest()
    {
        var d = new Notification(default, 1L) {
            Title = "Bob @ Good chat",
            Content = "Sent an image",
            HandledAt = Moment.Now,
        };
        d.AssertPassesThroughAllSerializers();

        d = new Notification(default, 1L) {
            Title = "Bob @ Good chat",
            Content = "Sent an image",
            HandledAt = null,
        };
        d.AssertPassesThroughAllSerializers();
    }
}
