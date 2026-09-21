using ActualChat.Testing.Host;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public sealed class NotificationMaintenanceTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData(MaintenanceMode.System)]
    [InlineData(MaintenanceMode.Import)]
    public async Task MaintenanceShouldSuppressNotificationCreationAndDelivery(MaintenanceMode mode)
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var owner = await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(true);
        var notification = MessageNotification.New(owner.Id, chatId, 1) with { Title = "blocked", Text = "blocked" };
        var sink = AppHost.Services.GetRequiredService<FirebaseMessagingTestSink>();
        var deviceId = new Symbol("import-test-" + owner.Id.Value);
        await Commander.Call(new NotificationsBackend_RegisterDevice(
            owner.Id, deviceId, DeviceType.WebBrowser, Symbol.Empty));
        await Commander.Call(new NotificationsBackend_Notify(notification));
        (await tester.NotificationsBackend.GetUserNotificationInfo(owner.Id, default)).Items.Should().ContainSingle();
        await Commander.Call(new MaintenancesBackend_Set(chatId.ToMaintenanceKey(), mode));
        sink.Clear();

        // act
        await Commander.Call(new NotificationsBackend_Notify(notification));
        await Commander.Call(new NotificationsBackend_Push(notification));

        // assert
        var info = await tester.NotificationsBackend.GetUserNotificationInfo(owner.Id, default);
        info.Items.Should().BeEmpty();
        sink.Messages.Should().NotContain(x => !x.IsDismissal && x.DeviceIds.Contains(deviceId));
        await Commander.Call(new MaintenancesBackend_Set(chatId.ToMaintenanceKey(), MaintenanceMode.None));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DelayedImportedEventsShouldRemainSilent(bool importedEntry, bool suppressedEvent)
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var owner = await tester.SignInAsUniqueAlice();
        var (chatId, _) = await tester.CreateChat(true);
        var author = (await tester.Authors.GetOwn(tester.Session, chatId, default))!;
        var entry = new TextEntry(ChatEntryId.New(chatId, 999), 1) {
            AuthorId = author.Id, BeginsAt = Moment.Now, Content = "historical",
            IsImported = importedEntry,
        };
        var change = new ChatEntryChangedEvent(entry, author, ChangeKind.Create, null) {
            SuppressNotifications = suppressedEvent,
        };

        // act
        await Commander.Call(change);

        // assert
        var info = await tester.NotificationsBackend.GetUserNotificationInfo(owner.Id, default);
        info.Items.Should().BeEmpty();
    }
}
