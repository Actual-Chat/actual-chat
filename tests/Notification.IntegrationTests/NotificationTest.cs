using ActualChat.Testing.Host;

namespace ActualChat.Notification.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public class NotificationTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ParallelInsertsWithSameIdAreSafe()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsBob();

        var notificationId = new NotificationId(account.Id, NotificationKind.Message, Constants.Chat.DefaultChatId);
        var notification = new Notification(notificationId) {
            Title = "Notify",
            Content = "Hello",
        };

        var tasks = new List<Task<bool>>();
        for (int i = 0; i < 20; i++) {
            var upsert = new NotificationsBackend_Upsert(notification);
            // ReSharper disable once AccessToDisposedClosure
            var task = Task.Run(() => tester.Commander.Call(upsert, true));
            tasks.Add(task);
        }

        var results = await Task.WhenAll(tasks);
        Out.WriteLine(string.Join(", ", results));

        results.Should().ContainSingle(r => r);
    }

    [Fact]
    public async Task ParallelNotificationsAreSafe()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var account = await tester.SignInAsBob();

        var notificationId = new NotificationId(account.Id, NotificationKind.Message, Constants.Chat.DefaultChatId);
        var notification = new Notification(notificationId) {
            Title = "Notify",
            Content = "Hello",
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
