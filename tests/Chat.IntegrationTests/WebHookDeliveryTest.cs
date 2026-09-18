using System.Net;
using ActualChat.Chat.Db;
using ActualChat.Security;
using ActualChat.Testing.Host;
using ActualChat.WebHooks;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class WebHookDeliveryTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SignatureTolerance = TimeSpan.FromMinutes(5);

    private WebClientTester Alice => field ??= fixture.AppHost.NewWebClientTester(Out);
    private IWebHooksBackend Backend => field ??= AppHost.Services.GetRequiredService<IWebHooksBackend>();
    private WebHookReceiver Receiver { get; } = new();

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Alice.SignInAsAlice();
    }

    protected override async Task DisposeAsync()
    {
        await Receiver.DisposeAsync();
        await Alice.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task DeliveryShouldBeSignedAndOrdered()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Delivery signed" });
        var (hook, secret) = await CreateHook(chatId);

        // act
        var entries = new List<ChatEntry>();
        for (var i = 1; i <= 3; i++)
            entries.Add(await Alice.CreateTextEntry(chatId, $"hook message {i}"));

        // assert
        var receivedIds = new List<string>();
        var messageIds = new List<long>();
        for (var i = 0; i < 3; i++) {
            var received = await Receiver.Next(ReceiveTimeout);
            received.Headers.Should().ContainKey("user-agent").WhoseValue.Should().Be("Voxt-Hooks/1");
            received.Headers.Should().ContainKey("content-type").WhoseValue.Should().StartWith("application/json");
            var id = received.Headers.Should().ContainKey("webhook-id").WhoseValue;
            var timestamp = long.Parse(received.Headers.Should().ContainKey("webhook-timestamp").WhoseValue);
            var signature = received.Headers.Should().ContainKey("webhook-signature").WhoseValue;
            StandardWebhookSigner
                .Verify(secret, id, timestamp, received.Body, signature, SignatureTolerance, Clocks.SystemClock.Now)
                .Should().BeTrue("the receiver must be able to verify the signature with the secret it was given");
            using var doc = JsonDocument.Parse(received.Body);
            doc.RootElement.GetProperty("id").GetString().Should().Be(id, "the header and the envelope id agree");
            doc.RootElement.GetProperty("type").GetString().Should().Be("message.posted");
            receivedIds.Add(id);
            messageIds.Add(doc.RootElement.GetProperty("data").GetProperty("message").GetProperty("id").GetInt64());
        }
        messageIds.Should().BeEquivalentTo(entries.Select(x => x.LocalId), "every posted message gets delivered once");
        var deliveries = await ComputedTest.When(async ct => {
            var items = await Backend.ListDeliveries(hook.Id, Constants.WebHooks.DeliveryListLimit, ct);
            items.Should().HaveCount(3);
            items.Should().OnlyContain(x => x.Status == WebHookDeliveryStatus.Succeeded);
            items.Should().OnlyContain(x => x.Attempts == 1 && x.LastStatusCode == 200 && x.CompletedAt != null);
            (await Backend.Get(hook.Id, ct))!.ConsecutiveFailures.Should().Be(0);
            return items;
        }, ReceiveTimeout);
        // The event queue fans the posts in concurrently, so the outbox order (Seq) is the contract, not the post order
        receivedIds.Should().Equal(
            deliveries.OrderBy(x => x.Seq).Select(x => x.Id),
            "deliveries follow the outbox order");
    }

    [Fact]
    public async Task ServerErrorShouldScheduleRetry()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Delivery retry" });
        var (hook, _) = await CreateHook(chatId);
        Receiver.StatusFor = index => index == 0 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK;

        // act
        var sentAt = Clocks.SystemClock.Now;
        await Alice.CreateTextEntry(chatId, "retry me");
        await Receiver.Next(ReceiveTimeout);

        // assert
        await ComputedTest.When(async ct => {
            var delivery = (await Backend.ListDeliveries(hook.Id, Constants.WebHooks.DeliveryListLimit, ct)).Single();
            delivery.Status.Should().Be(WebHookDeliveryStatus.Pending);
            delivery.Attempts.Should().Be(1);
            delivery.LastStatusCode.Should().Be(500);
            delivery.LastError.Should().StartWith("500");
            delivery.CompletedAt.Should().BeNull();
            delivery.NextAttemptAt.Should().NotBeNull();
            (delivery.NextAttemptAt!.Value - (sentAt + Constants.WebHooks.RetryDelays[0])).Duration()
                .Should().BeLessThan(TimeSpan.FromSeconds(10), "the first retry comes after the first delay");
            var updatedHook = (await Backend.Get(hook.Id, ct))!;
            updatedHook.IsEnabled.Should().BeTrue();
            updatedHook.ConsecutiveFailures.Should().Be(1);
            updatedHook.LastStatusCode.Should().Be(500);
        }, ReceiveTimeout);
    }

    [Fact]
    public async Task GoneShouldDisableHook()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Delivery gone" });
        var (hook, _) = await CreateHook(chatId);
        Receiver.StatusFor = _ => HttpStatusCode.Gone;

        // act
        await Alice.CreateTextEntry(chatId, "gone");
        await Receiver.Next(ReceiveTimeout);

        // assert
        await ComputedTest.When(async ct => {
            var updatedHook = (await Backend.Get(hook.Id, ct))!;
            updatedHook.IsEnabled.Should().BeFalse();
            updatedHook.DisabledReason.Should().Be(WebHookDisabledReason.DeliveryFailures);
            updatedHook.LastStatusCode.Should().Be(410);
            var delivery = (await Backend.ListDeliveries(hook.Id, Constants.WebHooks.DeliveryListLimit, ct)).Single();
            delivery.Status.Should().Be(WebHookDeliveryStatus.Failed);
        }, ReceiveTimeout);
    }

    [Fact]
    public async Task ClientErrorShouldFailWithoutRetry()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Delivery 404" });
        var (hook, _) = await CreateHook(chatId);
        Receiver.StatusFor = index => index == 0 ? HttpStatusCode.NotFound : HttpStatusCode.OK;

        // act
        await Alice.CreateTextEntry(chatId, "first, fails");
        await Alice.CreateTextEntry(chatId, "second, lands");

        // assert
        var first = await Receiver.Next(ReceiveTimeout);
        first.Body.Should().Contain("first, fails");
        var second = await Receiver.Next(ReceiveTimeout);
        second.Body.Should().Contain("second, lands", "a terminal failure doesn't block the ones behind it");
        await ComputedTest.When(async ct => {
            var deliveries = await Backend.ListDeliveries(hook.Id, Constants.WebHooks.DeliveryListLimit, ct);
            deliveries.Should().HaveCount(2);
            var failed = deliveries.Single(x => x.LastStatusCode == 404);
            failed.Status.Should().Be(WebHookDeliveryStatus.Failed);
            failed.Attempts.Should().Be(1);
            failed.NextAttemptAt.Should().BeNull();
            failed.CompletedAt.Should().NotBeNull();
            deliveries.Single(x => x.Id != failed.Id).Status.Should().Be(WebHookDeliveryStatus.Succeeded);
            (await Backend.Get(hook.Id, ct))!.IsEnabled.Should().BeTrue();
        }, ReceiveTimeout);
    }

    [Fact]
    public async Task StaleHeadOfLineShouldDisableAndAbandon()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Delivery stale" });
        var (hook, _) = await CreateHook(chatId);
        Receiver.StatusFor = _ => HttpStatusCode.InternalServerError;
        var staleId = $"{hook.Id}:stale";
        var staleCreatedAt = Clocks.SystemClock.Now - Constants.WebHooks.DisableAfter - TimeSpan.FromHours(1);
        var dbHub = AppHost.Services.DbHub<ChatDbContext>();
        await using (var dbContext = await dbHub.CreateDbContext(readWrite: true)) {
            dbContext.Add(new DbWebHookDelivery {
                Id = staleId,
                WebHookId = hook.Id.Value,
                Seq = dbHub.VersionGenerator.NextVersion(),
                EventType = "message.posted",
                Payload = "{}",
                Status = WebHookDeliveryStatus.Pending,
                Attempts = 3,
                CreatedAt = staleCreatedAt.ToDateTime(),
            });
            await dbContext.SaveChangesAsync();
        }

        // act
        await Alice.CreateTextEntry(chatId, "behind the stale one");
        var received = await Receiver.Next(ReceiveTimeout);

        // assert
        received.Headers["webhook-id"].Should().Be(staleId, "the head of the line goes first");
        await ComputedTest.When(async ct => {
            var updatedHook = (await Backend.Get(hook.Id, ct))!;
            updatedHook.IsEnabled.Should().BeFalse();
            updatedHook.DisabledReason.Should().Be(WebHookDisabledReason.DeliveryFailures);
            var deliveries = await Backend.ListDeliveries(hook.Id, Constants.WebHooks.DeliveryListLimit, ct);
            deliveries.Should().HaveCount(2);
            deliveries.Should().OnlyContain(x => x.Status == WebHookDeliveryStatus.Abandoned);
        }, ReceiveTimeout);
    }

    [Fact]
    public async Task RotatedSecretShouldSendTwoSignatures()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Delivery rotate" });
        var (hook, oldSecret) = await CreateHook(chatId);
        var newSecret = await Commander.Call(new WebHooksBackend_RotateSecret(hook.Id, chatId.Value));

        // act
        await Alice.CreateTextEntry(chatId, "signed twice");
        var received = await Receiver.Next(ReceiveTimeout);

        // assert
        var id = received.Headers["webhook-id"];
        var timestamp = long.Parse(received.Headers["webhook-timestamp"]);
        var signature = received.Headers["webhook-signature"];
        signature.Split(' ').Should().HaveCount(2, "the old secret stays valid through the overlap window");
        var now = Clocks.SystemClock.Now;
        StandardWebhookSigner.Verify(newSecret, id, timestamp, received.Body, signature, SignatureTolerance, now)
            .Should().BeTrue();
        StandardWebhookSigner.Verify(oldSecret, id, timestamp, received.Body, signature, SignatureTolerance, now)
            .Should().BeTrue();
    }

    [Fact]
    public async Task TestEventShouldNotTouchOutbox()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Delivery ping" });
        var (hook, secret) = await CreateHook(chatId);
        var alice = await Alice.GetOwnAccount();

        // act
        var result = await Alice.Commander.Call(new WebHooks_Test { Session = Alice.Session, Id = hook.Id });

        // assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Error.Should().BeNull();
        var received = await Receiver.Next(ReceiveTimeout);
        using var doc = JsonDocument.Parse(received.Body);
        doc.RootElement.GetProperty("type").GetString().Should().Be("ping");
        doc.RootElement.GetProperty("data").GetProperty("sentBy").GetString().Should().Be(alice.Avatar.Name);
        StandardWebhookSigner
            .Verify(
                secret,
                received.Headers["webhook-id"],
                long.Parse(received.Headers["webhook-timestamp"]),
                received.Body,
                received.Headers["webhook-signature"],
                SignatureTolerance,
                Clocks.SystemClock.Now)
            .Should().BeTrue();
        (await Backend.ListDeliveries(hook.Id, Constants.WebHooks.DeliveryListLimit, default))
            .Should().BeEmpty("a test event bypasses the outbox");
    }

    [Fact]
    public async Task RedeliverShouldCloneRow()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Delivery redeliver" });
        var (hook, _) = await CreateHook(chatId);
        Receiver.StatusFor = index => index == 0 ? HttpStatusCode.NotFound : HttpStatusCode.OK;
        await Alice.CreateTextEntry(chatId, "try again");
        var first = await Receiver.Next(ReceiveTimeout);
        var deliveryId = first.Headers["webhook-id"];
        await ComputedTest.When(async ct => {
            var deliveries = await Backend.ListDeliveries(hook.Id, Constants.WebHooks.DeliveryListLimit, ct);
            deliveries.Single().Status.Should().Be(WebHookDeliveryStatus.Failed);
        }, ReceiveTimeout);

        // act
        await Alice.Commander.Call(new WebHooks_Redeliver {
            Session = Alice.Session, Id = hook.Id, DeliveryId = deliveryId,
        });

        // assert
        var second = await Receiver.Next(ReceiveTimeout);
        second.Headers["webhook-id"].Should().Be($"{deliveryId}:r1");
        second.Body.Should().Be(first.Body, "a redelivery carries the original payload");
        await ComputedTest.When(async ct => {
            var deliveries = await Backend.ListDeliveries(hook.Id, Constants.WebHooks.DeliveryListLimit, ct);
            deliveries.Should().HaveCount(2);
            deliveries.Single(x => x.Id == $"{deliveryId}:r1").Status.Should().Be(WebHookDeliveryStatus.Succeeded);
            deliveries.Single(x => x.Id == deliveryId).Status.Should().Be(WebHookDeliveryStatus.Failed);
        }, ReceiveTimeout);
    }

    // Private methods

    private async Task<(WebHook Hook, string Secret)> CreateHook(ChatId chatId)
    {
        var alice = await Alice.GetOwnAccount();
        var diff = new WebHookDiff { Name = "CI", Url = Receiver.HookUrl, Events = WebHookEvents.Messages };
        var result = await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null, Change.Create(diff), alice.Id));
        var hook = result.WebHook!;
        await ComputedTest.When(async ct
            => (await Backend.ListActiveForChat(chatId, ct)).Should().Contain(x => x.Id == hook.Id));
        return (hook, result.Secret!);
    }
}
