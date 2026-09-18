using ActualChat.Chat.Db;
using ActualChat.Security;
using ActualChat.Testing.Host;
using ActualChat.WebHooks;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Generators;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class WebHooksBackendTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Alice => field ??= fixture.AppHost.NewWebClientTester(Out);
    private IWebHooksBackend Backend => field ??= AppHost.Services.GetRequiredService<IWebHooksBackend>();

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Alice.SignInAsAlice();
    }

    protected override async Task DisposeAsync()
    {
        await Alice.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task CreateShouldReturnSecretOnceAndListByScope()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Hooks" });
        var alice = await Alice.GetOwnAccount();

        // act
        var result = await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff()),
            alice.Id));

        // assert
        result.Secret.Should().StartWith(StandardWebhookSigner.SecretPrefix);
        var webHook = result.WebHook!;
        webHook.Kind.Should().Be(WebHookKind.Outgoing);
        webHook.IsEnabled.Should().BeTrue();
        webHook.CreatedBy.Should().Be(alice.Id);
        webHook.Events.Should().Be(WebHookEvents.Messages);
        await ComputedTest.When(async ct => {
            var hooks = await Backend.ListByScope(WebHookScope.Chat, chatId.Value, ct);
            hooks.Should().ContainSingle(x => x.Id == webHook.Id);
        });
        (await Backend.Get(webHook.Id, default))!.Name.Should().Be("CI");
        (await Backend.ListActiveForChat(chatId, default)).Should().ContainSingle(x => x.Id == webHook.Id);
    }

    [Fact]
    public async Task UpdateAndRemoveShouldInvalidateLists()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Hooks update" });
        var alice = await Alice.GetOwnAccount();
        var created = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff()),
            alice.Id))).WebHook!;
        await ComputedTest.When(async ct
            => (await Backend.ListActiveForChat(chatId, ct)).Should().ContainSingle(x => x.Id == created.Id));

        // act - update
        var updated = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, created.Id, created.Version,
            Change.Update(new WebHookDiff { Name = "CI 2", IsEnabled = false }),
            alice.Id))).WebHook!;

        // assert - the disabled hook leaves the active list, Get sees the new name
        updated.Name.Should().Be("CI 2");
        updated.IsEnabled.Should().BeFalse();
        updated.DisabledReason.Should().Be(WebHookDisabledReason.Manual);
        updated.Version.Should().BeGreaterThan(created.Version);
        await ComputedTest.When(async ct => {
            (await Backend.ListActiveForChat(chatId, ct)).Should().BeEmpty();
            (await Backend.Get(created.Id, ct))!.Name.Should().Be("CI 2");
        });

        // act - remove, with a delivery in the outbox
        await Commander.Call(NewEnqueue(created.Id, chatId, $"{created.Id}:d1"));
        await ComputedTest.When(async ct => {
            var deliveries = await Backend.ListDeliveries(created.Id, Constants.WebHooks.DeliveryListLimit, ct);
            deliveries.Should().ContainSingle();
        });
        var removed = await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, created.Id, null, Change.Remove<WebHookDiff>(), alice.Id));

        // assert
        removed.WebHook.Should().BeNull();
        await ComputedTest.When(async ct => {
            (await Backend.Get(created.Id, ct)).Should().BeNull();
            (await Backend.ListByScope(WebHookScope.Chat, chatId.Value, ct)).Should().BeEmpty();
            (await Backend.ListDeliveries(created.Id, Constants.WebHooks.DeliveryListLimit, ct))
                .Should().BeEmpty("deliveries die with the hook and their computed must be invalidated");
        });
    }

    [Fact]
    public async Task PlaceHookShouldBeListedForPlaceChats()
    {
        // arrange
        var alice = await Alice.GetOwnAccount();
        var place = await Alice.CreatePlace(true, "Hooks place");
        var (chatId, _) = await Alice.CreateChat(true, "Hooks place chat", place.Id);
        var (otherChatId, _) = await Alice.CreateChat(x => x with { Title = "Hooks other" });

        // act
        var created = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Place, place.Id.Value, null, null,
            Change.Create(NewDiff("Place CI", events: WebHookEvents.Members)),
            alice.Id))).WebHook!;

        // assert
        created.Scope.Should().Be(WebHookScope.Place);
        await ComputedTest.When(async ct
            => (await Backend.ListActiveForChat(chatId, ct)).Should().ContainSingle(x => x.Id == created.Id));
        (await Backend.ListActiveForChat(otherChatId, default)).Should().NotContain(x => x.Id == created.Id);
    }

    [Fact]
    public async Task UserHookShouldBeListedForUser()
    {
        // arrange
        var alice = await Alice.GetOwnAccount();

        // act
        var created = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.User, alice.Id.Value, null, null,
            Change.Create(NewDiff("Me", events: WebHookEvents.Notification) with { SubscribeNotifications = true }),
            alice.Id))).WebHook!;

        // assert
        await ComputedTest.When(async ct
            => (await Backend.ListActiveForUser(alice.Id, ct)).Should().ContainSingle(x => x.Id == created.Id));
    }

    [Fact]
    public async Task RotateSecretShouldKeepOldOneForOverlap()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Hooks rotate" });
        var alice = await Alice.GetOwnAccount();
        var result = await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff()),
            alice.Id));
        var webHook = result.WebHook!;
        var secrets = AppHost.Services.GetRequiredService<WebHookSecrets>();

        // act
        var newSecret = await Commander.Call(new WebHooksBackend_RotateSecret(webHook.Id, chatId.Value));

        // assert
        newSecret.Should().StartWith(StandardWebhookSigner.SecretPrefix);
        newSecret.Should().NotBe(result.Secret);
        var dbHub = AppHost.Services.DbHub<ChatDbContext>();
        await using var dbContext = await dbHub.CreateDbContext();
        var dbWebHook = await dbContext.WebHooks.FirstAsync(x => x.Id == webHook.Id.Value);
        dbWebHook.Version.Should().BeGreaterThan(webHook.Version);
        var now = Clocks.SystemClock.Now;
        secrets.GetSigningSecrets(dbWebHook, now).Should().Equal(newSecret, result.Secret!);
        secrets.GetSigningSecrets(dbWebHook, now + TimeSpan.FromHours(25)).Should().Equal(newSecret);
        (await Backend.Get(webHook.Id, default))!.Version.Should().Be(dbWebHook.Version);
    }

    [Fact]
    public async Task CustomHeaderValueShouldBeStoredProtected()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Hooks header" });
        var alice = await Alice.GetOwnAccount();
        var secrets = AppHost.Services.GetRequiredService<WebHookSecrets>();

        // act
        var webHook = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff() with { CustomHeaderName = "X-Token", CustomHeaderValue = "t0ken" }),
            alice.Id))).WebHook!;

        // assert
        webHook.CustomHeaderName.Should().Be("X-Token");
        var dbHub = AppHost.Services.DbHub<ChatDbContext>();
        await using var dbContext = await dbHub.CreateDbContext();
        var dbWebHook = await dbContext.WebHooks.FirstAsync(x => x.Id == webHook.Id.Value);
        dbWebHook.CustomHeaderValueProtected.Should().NotBeNullOrEmpty().And.NotBe("t0ken");
        secrets.GetCustomHeaderValue(dbWebHook).Should().Be("t0ken");
    }

    [Fact]
    public async Task CreateShouldRejectHttpUrlAndEmptyEvents()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Hooks invalid" });
        var alice = await Alice.GetOwnAccount();

        // act
        var httpUrl = () => Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff(url: "http://example.com/hook")),
            alice.Id));
        var noEvents = () => Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff(events: WebHookEvents.None)),
            alice.Id));
        var emptyName = () => Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff(" ")),
            alice.Id));
        var userHookWithoutTargets = () => Commander.Call(new WebHooksBackend_Change(
            WebHookScope.User, alice.Id.Value, null, null,
            Change.Create(NewDiff("Me")),
            alice.Id));
        var reservedHeader = () => Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff() with { CustomHeaderName = "Webhook-Signature", CustomHeaderValue = "x" }),
            alice.Id));
        var ipLiteralUrl = () => Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff(url: "https://203.0.113.5/hook")),
            alice.Id));
        var headerNameWithSpace = () => Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff() with { CustomHeaderName = "X Token", CustomHeaderValue = "x" }),
            alice.Id));
        var headerValueWithNewLine = () => Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff() with { CustomHeaderName = "X-Token", CustomHeaderValue = "x\r\nHost: evil" }),
            alice.Id));

        // assert
        await httpUrl.Should().ThrowAsync<InvalidOperationException>().WithMessage("*https*");
        await ipLiteralUrl.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IP address*");
        await headerNameWithSpace.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Header name*");
        await headerValueWithNewLine.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Header value*");
        await noEvents.Should().ThrowAsync<InvalidOperationException>().WithMessage("*event*");
        await emptyName.Should().ThrowAsync<InvalidOperationException>().WithMessage("*name*");
        await userHookWithoutTargets.Should().ThrowAsync<InvalidOperationException>();
        await reservedHeader.Should().ThrowAsync<InvalidOperationException>().WithMessage("*reserved*");
        (await Backend.ListByScope(WebHookScope.Chat, chatId.Value, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateShouldAllowLoopbackHttpUrlWhenTested()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Hooks loopback" });
        var alice = await Alice.GetOwnAccount();

        // act
        var webHook = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff("Local", "http://localhost:5555/hook", WebHookEvents.Ping)),
            alice.Id))).WebHook!;

        // assert
        webHook.Url.Should().Be("http://localhost:5555/hook");
    }

    [Fact]
    public async Task EnqueueShouldIgnoreDuplicateDeliveryId()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Hooks enqueue" });
        var alice = await Alice.GetOwnAccount();
        var webHook = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff() with { IsEnabled = false }),
            alice.Id))).WebHook!;
        var deliveryId = $"{webHook.Id}:{RandomStringGenerator.Default.Next()}";

        // act
        await Commander.Call(NewEnqueue(webHook.Id, chatId, deliveryId));
        await Commander.Call(NewEnqueue(webHook.Id, chatId, deliveryId));
        await Commander.Call(NewEnqueue(webHook.Id, chatId, deliveryId + "-2"));

        // assert
        var deliveries = await ComputedTest.When(async ct => {
            var items = await Backend.ListDeliveries(webHook.Id, Constants.WebHooks.DeliveryListLimit, ct);
            items.Should().HaveCount(2);
            return items;
        });
        deliveries[0].Id.Should().Be(deliveryId + "-2", "newest delivery goes first");
        deliveries[1].Id.Should().Be(deliveryId);
        deliveries.Should().OnlyContain(x => x.Status == WebHookDeliveryStatus.Pending && x.Attempts == 0);
    }

    [Fact]
    public async Task RecordDeliveryAndDisableShouldUpdateHookAndDeliveries()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Hooks record" });
        var alice = await Alice.GetOwnAccount();
        var webHook = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff() with { IsEnabled = false }),
            alice.Id))).WebHook!;
        var failedId = $"{webHook.Id}:failed";
        var pendingId = $"{webHook.Id}:pending";
        await Commander.Call(NewEnqueue(webHook.Id, chatId, failedId));
        await Commander.Call(NewEnqueue(webHook.Id, chatId, pendingId));

        // act - one failed attempt with a retry scheduled
        var nextAttemptAt = Clocks.SystemClock.Now + TimeSpan.FromMinutes(1);
        var longError = "boom\r\n" + new string('x', 2 * Constants.WebHooks.MaxErrorLength);
        var expectedError = "boom" + new string('x', Constants.WebHooks.MaxErrorLength - 4);
        await Commander.Call(new WebHooksBackend_RecordDelivery(
            webHook.Id, chatId.Value, failedId, WebHookDeliveryStatus.Pending, 503, longError, 120, nextAttemptAt));

        // assert
        await ComputedTest.When(async ct => {
            var hook = (await Backend.Get(webHook.Id, ct))!;
            hook.ConsecutiveFailures.Should().Be(1);
            hook.LastStatusCode.Should().Be(503);
            hook.LastError.Should().Be(expectedError, "receiver text is capped and stripped of control chars");
            hook.LastActivityAt.Should().NotBeNull();
            var delivery = (await Backend.ListDeliveries(webHook.Id, Constants.WebHooks.DeliveryListLimit, ct))
                .Single(x => x.Id == failedId);
            delivery.Attempts.Should().Be(1);
            delivery.LastStatusCode.Should().Be(503);
            delivery.LastError.Should().Be(expectedError);
            delivery.LastLatencyMs.Should().Be(120);
            delivery.NextAttemptAt.Should().NotBeNull();
            (delivery.NextAttemptAt!.Value - nextAttemptAt).Duration()
                .Should().BeLessThan(TimeSpan.FromMilliseconds(1), "the column has microsecond precision");
            delivery.CompletedAt.Should().BeNull();
        });

        // act - success resets the failure counter and completes the delivery
        await Commander.Call(new WebHooksBackend_RecordDelivery(
            webHook.Id, chatId.Value, failedId, WebHookDeliveryStatus.Succeeded, 200, null, 80, null));

        // assert
        await ComputedTest.When(async ct => {
            var hook = (await Backend.Get(webHook.Id, ct))!;
            hook.ConsecutiveFailures.Should().Be(0);
            hook.LastStatusCode.Should().Be(200);
            hook.LastError.Should().BeNull();
            var delivery = (await Backend.ListDeliveries(webHook.Id, Constants.WebHooks.DeliveryListLimit, ct))
                .Single(x => x.Id == failedId);
            delivery.Status.Should().Be(WebHookDeliveryStatus.Succeeded);
            delivery.Attempts.Should().Be(2);
            delivery.CompletedAt.Should().NotBeNull();
        });

        // act - disable abandons what's still pending
        await Commander.Call(new WebHooksBackend_Disable(
            webHook.Id, chatId.Value, WebHookDisabledReason.DeliveryFailures, "too many failures"));

        // assert
        await ComputedTest.When(async ct => {
            var hook = (await Backend.Get(webHook.Id, ct))!;
            hook.IsEnabled.Should().BeFalse();
            hook.DisabledReason.Should().Be(WebHookDisabledReason.DeliveryFailures);
            hook.LastError.Should().Be("too many failures");
            (await Backend.ListActiveForChat(chatId, ct)).Should().BeEmpty();
            var deliveries = await Backend.ListDeliveries(webHook.Id, Constants.WebHooks.DeliveryListLimit, ct);
            deliveries.Single(x => x.Id == pendingId).Status.Should().Be(WebHookDeliveryStatus.Abandoned);
            deliveries.Single(x => x.Id == failedId).Status.Should().Be(WebHookDeliveryStatus.Succeeded);
        });
    }

    [Fact]
    public async Task RecordDeliveryShouldNotSplitSurrogatePairInError()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Hooks surrogate error" });
        var alice = await Alice.GetOwnAccount();
        var webHook = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff() with { IsEnabled = false }),
            alice.Id))).WebHook!;
        var deliveryId = $"{webHook.Id}:d1";
        await Commander.Call(NewEnqueue(webHook.Id, chatId, deliveryId));
        // The emoji's high surrogate sits exactly at the cap, so a plain cut would leave it alone
        var error = new string('x', Constants.WebHooks.MaxErrorLength - 1) + "\U0001F600 and more";

        // act
        await Commander.Call(new WebHooksBackend_RecordDelivery(
            webHook.Id, chatId.Value, deliveryId, WebHookDeliveryStatus.Failed, 500, error, 10, null));

        // assert
        await ComputedTest.When(async ct => {
            var hook = (await Backend.Get(webHook.Id, ct))!;
            var delivery = (await Backend.ListDeliveries(webHook.Id, Constants.WebHooks.DeliveryListLimit, ct))
                .Single(x => x.Id == deliveryId);
            foreach (var lastError in new[] { hook.LastError!, delivery.LastError! }) {
                lastError.Length.Should().BeLessThanOrEqualTo(Constants.WebHooks.MaxErrorLength);
                char.IsHighSurrogate(lastError[^1]).Should().BeFalse("the cut backs off to a full pair");
                lastError.Should().Be(new string('x', Constants.WebHooks.MaxErrorLength - 1));
            }
        });
    }

    [Fact]
    public async Task RedeliverShouldCloneDeliveryAsPending()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Hooks redeliver" });
        var alice = await Alice.GetOwnAccount();
        var webHook = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(NewDiff() with { IsEnabled = false }),
            alice.Id))).WebHook!;
        var deliveryId = $"{webHook.Id}:d1";
        await Commander.Call(NewEnqueue(webHook.Id, chatId, deliveryId));
        var redeliverPending = ()
            => Commander.Call(new WebHooksBackend_Redeliver(webHook.Id, chatId.Value, deliveryId));
        await redeliverPending.Should().ThrowAsync<InvalidOperationException>().WithMessage("*completed*");
        await Commander.Call(new WebHooksBackend_RecordDelivery(
            webHook.Id, chatId.Value, deliveryId, WebHookDeliveryStatus.Failed, 500, "boom", 10, null));

        // act
        await Commander.Call(new WebHooksBackend_Redeliver(webHook.Id, chatId.Value, deliveryId));

        // assert
        await ComputedTest.When(async ct => {
            var deliveries = await Backend.ListDeliveries(webHook.Id, Constants.WebHooks.DeliveryListLimit, ct);
            deliveries.Should().HaveCount(2);
            var original = deliveries.Single(x => x.Id == deliveryId);
            original.Status.Should().Be(WebHookDeliveryStatus.Failed);
            var clone = deliveries.Single(x => x.Id == $"{deliveryId}:r1");
            clone.Status.Should().Be(WebHookDeliveryStatus.Pending);
            clone.Attempts.Should().Be(0);
            clone.EventType.Should().Be("message.posted");
            clone.Seq.Should().BeGreaterThan(original.Seq);
            clone.LastError.Should().BeNull();
            clone.CompletedAt.Should().BeNull();
            deliveries[0].Id.Should().Be(clone.Id, "newest delivery goes first");
        });
    }

    // Private methods

    // A hook that gets outbox rows is created disabled: the delivery flow then leaves the rows
    // alone, so the tests see exactly what the backend commands wrote
    private static WebHookDiff NewDiff(
        string name = "CI",
        string url = "https://example.com/hook",
        WebHookEvents events = WebHookEvents.Messages)
        => new() { Name = name, Url = url, Events = events };

    private static WebHooksBackend_Enqueue NewEnqueue(WebHookId id, ChatId chatId, string deliveryId)
        => new(id, chatId.Value, deliveryId, "message.posted", "{}");
}
