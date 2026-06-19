using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class NotificationTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ParallelNotificationsAreSafe()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsBob();

        var notification = MessageNotification.New(account.Id, Constants.Chat.DefaultChatId) with {
            Title = "Notify",
            Text = "Hello",
        };

        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++) {
            var upsert = new NotificationsBackend_Notify(notification);
            // ReSharper disable once AccessToDisposedClosure
            var task = Task.Run(() => tester.Commander.Call(upsert, true));
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
    }
}
