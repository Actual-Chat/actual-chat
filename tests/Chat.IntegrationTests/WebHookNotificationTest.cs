using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualChat.WebHooks;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class WebHookNotificationTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(5);

    private WebClientTester Alice => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Bob => field ??= fixture.AppHost.NewWebClientTester(Out);
    private IWebHooksBackend Backend => field ??= AppHost.Services.GetRequiredService<IWebHooksBackend>();
    private WebHookReceiver Receiver { get; } = new();

    protected override async Task DisposeAsync()
    {
        await Receiver.DisposeAsync();
        await Alice.DisposeSilentlyAsync();
        await Bob.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task MentionShouldDeliverNotificationToPersonalHook()
    {
        // arrange
        await Alice.SignInAsAlice();
        var bobAccount = await Bob.SignInAsBob();
        var (chatId, inviteId) = await Alice.CreateChat(x => x with { Title = "Personal notification hook" });
        var bobAuthor = await Bob.JoinChat(chatId, inviteId);
        await CreateNotificationHook(bobAccount.Id);

        // act
        await Alice.CreateTextEntry(chatId, $"hi @a:{bobAuthor.Id}");

        // assert
        var received = await Receiver.Next(ReceiveTimeout);
        using var doc = JsonDocument.Parse(received.Body);
        var root = doc.RootElement;
        root.GetProperty("type").GetString().Should().Be("notification");
        var data = root.GetProperty("data");
        data.GetProperty("kind").GetString().Should().Be("mention");
        data.GetProperty("message").GetProperty("text").GetString().Should().Contain("hi");
    }

    [Fact]
    public async Task MutedChatShouldNotDeliver()
    {
        // arrange
        await Alice.SignInAsAlice();
        var bobAccount = await Bob.SignInAsBob();
        var (chatId, inviteId) = await Alice.CreateChat(x => x with { Title = "Muted notification hook" });
        await Bob.JoinChat(chatId, inviteId);
        var hook = await CreateNotificationHook(bobAccount.Id);
        await SetNotificationMode(Bob, chatId, ChatNotificationMode.Muted);

        // act
        await Alice.CreateTextEntry(chatId, "plain message");

        // assert
        var receive = () => Receiver.Next(SilenceTimeout);
        await receive.Should().ThrowAsync<OperationCanceledException>("a muted chat must not alert Bob at all");
        (await Backend.ListDeliveries(hook.Id, Constants.WebHooks.DeliveryListLimit, default))
            .Should().BeEmpty();
    }

    // Private methods

    private async Task<WebHook> CreateNotificationHook(UserId userId)
    {
        var diff = new WebHookDiff {
            Name = "CI", Url = Receiver.HookUrl, Events = WebHookEvents.Notification, SubscribeNotifications = true,
        };
        var result = await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.User, userId.Value, null, null, Change.Create(diff), userId));
        var hook = result.WebHook!;
        await ComputedTest.When(async ct
            => (await Backend.ListActiveForUser(userId, ct)).Should().Contain(x => x.Id == hook.Id));
        return hook;
    }

    private static Task SetNotificationMode(IWebTester tester, ChatId chatId, ChatNotificationMode mode)
        => tester.AppServices.UserSettingsUI(tester.Session).ChatUserSettings(chatId)
            .Update(x => x with { NotificationMode = mode }, CancellationToken.None);
}
