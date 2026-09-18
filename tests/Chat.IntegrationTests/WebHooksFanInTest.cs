using ActualChat.Chat.Db;
using ActualChat.Testing.Host;
using ActualChat.WebHooks;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Testing.Web;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class WebHooksFanInTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    // Nothing listens there: the delivery flow's attempt is refused at once, and the rows stay Pending
    private static readonly string HookUrl = $"http://localhost:{WebTestHelpers.GetUnusedTcpPort()}/hook";

    private readonly List<WebHook> _createdHooks = [];

    private WebClientTester Alice => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Bob => field ??= fixture.AppHost.NewWebClientTester(Out);
    private IWebHooksBackend Backend => field ??= AppHost.Services.GetRequiredService<IWebHooksBackend>();

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Alice.SignInAsAlice();
        await Bob.SignInAsBob();
    }

    protected override async Task DisposeAsync()
    {
        // The hooks point at a port nothing listens on; removed, they can't retry into a later test's receiver
        foreach (var hook in _createdHooks)
            await Commander.Call(new WebHooksBackend_Change(
                    hook.Scope, hook.ScopeId, hook.Id, null, Change.Remove<WebHookDiff>(), hook.CreatedBy))
                .SilentAwait();
        await Alice.DisposeSilentlyAsync();
        await Bob.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task PostedMessageShouldEnqueueOneDelivery()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Fan-in posted" });
        var hook = await CreateHook(Alice, WebHookScope.Chat, chatId.Value, WebHookEvents.Messages);

        // act
        var entry = await Alice.CreateTextEntry(chatId, "hello hooks");

        // assert
        var deliveries = await WaitForDeliveries(hook.Id, 1);
        var delivery = deliveries.Single();
        delivery.EventType.Should().Be("message.posted");
        delivery.Status.Should().Be(WebHookDeliveryStatus.Pending);
        delivery.Id.Should()
            .Be(WebHookPayloads.DeliveryId(hook.Id, "message.posted", $"{entry.LocalId}:{entry.Version}"));
        var payload = await ReadPayload(delivery.Id);
        payload.Should().Contain("hello hooks");
        payload.Should().Contain(chatId.Value);
        EnvelopeId(payload).Should().Be(delivery.Id, "the webhook-id header and the body id must agree");
    }

    [Fact]
    public async Task RemovalViaUpdateAndRestoreShouldEnqueue()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Fan-in removal via update" });
        var hook = await CreateHook(Alice, WebHookScope.Chat, chatId.Value, WebHookEvents.Messages);
        var entry = await Alice.CreateTextEntry(chatId, "soon gone");
        await WaitForDeliveries(hook.Id, 1);

        // act - a thread start is removed through an Update flipping IsRemoved, not a Remove
        await Commander.Call(new ChatsBackend_ChangeEntry(
            entry.Id, null, Change.Update(new ChatEntryDiff { IsRemoved = true })));

        // assert
        var deliveries = await WaitForDeliveries(hook.Id, 2);
        deliveries[0].EventType.Should().Be("message.removed");

        // act - restore flips it back
        await Commander.Call(new Chats_RestoreEntry {
            Session = Alice.Session, ChatId = chatId, LocalId = entry.LocalId,
        });

        // assert
        deliveries = await WaitForDeliveries(hook.Id, 3);
        deliveries[0].EventType.Should().Be("message.posted");
        deliveries[0].Id.Should().NotBe(deliveries[2].Id, "the restored entry has a new version");
        (await ReadPayload(deliveries[0].Id)).Should().Contain("soon gone");
    }

    [Fact]
    public async Task ReactionRemovedAndReAddedShouldEnqueueTwoAdds()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Fan-in reaction re-add" });
        var hook = await CreateHook(Alice, WebHookScope.Chat, chatId.Value, WebHookEvents.Reactions);
        var entry = await Alice.CreateTextEntry(chatId, "react to me");

        // act - the same emoji toggles the reaction: add, remove, add again
        await Alice.React(entry.Id, Emojis.Love);
        await WaitForDeliveries(hook.Id, 1);
        await Alice.React(entry.Id, Emojis.Love);
        await WaitForDeliveries(hook.Id, 2);
        await Alice.React(entry.Id, Emojis.Love);

        // assert - the reaction id is (entry, author), so only the version can tell the re-add apart
        var deliveries = await WaitForDeliveries(hook.Id, 3);
        deliveries.Select(x => x.EventType).Should().Equal("reaction.added", "reaction.removed", "reaction.added");
        deliveries.Select(x => x.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task PlaceMemberJoinedShouldEnqueueWithMatchingIds()
    {
        // arrange
        var place = await Alice.CreatePlace(true, "Fan-in place members");
        var hook = await CreateHook(Alice, WebHookScope.Place, place.Id.Value, WebHookEvents.PlaceChanges);
        var bob = await Bob.GetOwnAccount();

        // act
        await Bob.JoinPlace(place.Id);

        // assert
        var bobAuthor = await ComputedTest.When(async ct => {
            var author = await AppHost.Services.GetRequiredService<IAuthorsBackend>()
                .GetByUserId(place.Id.RootChatId, bob.Id, RequestedAuthorKind.Full, ct);
            author.Should().NotBeNull();
            return author!;
        });
        var delivery = await ComputedTest.When(async ct => {
            var deliveries = await Backend.ListDeliveries(hook.Id, Constants.WebHooks.DeliveryListLimit, ct);
            return deliveries.Single(x => x.EventType == "place.member.joined" && x.Id.Contains(bobAuthor.Id.Value));
        }, TimeSpan.FromSeconds(10));
        delivery.Id.Should().Be(WebHookPayloads.DeliveryId(
            hook.Id, "place.member.joined", $"{bobAuthor.Id.Value}:{bobAuthor.Version}"));
        var payload = await ReadPayload(delivery.Id);
        EnvelopeId(payload).Should().Be(delivery.Id, "the webhook-id header and the body id must agree");
        payload.Should().Contain(bobAuthor.Id.Value);
    }

    [Fact]
    public async Task EditShouldEnqueueEditedWithPrevious()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Fan-in edited" });
        var hook = await CreateHook(Alice, WebHookScope.Chat, chatId.Value, WebHookEvents.Messages);
        var entry = await Alice.CreateTextEntry(chatId, "first take");
        await WaitForDeliveries(hook.Id, 1);

        // act
        await Alice.UpdateTextEntry(entry.Id, "second take");

        // assert
        var deliveries = await WaitForDeliveries(hook.Id, 2);
        var edited = deliveries.Single(x => x.EventType == "message.edited");
        var payload = await ReadPayload(edited.Id);
        payload.Should().Contain("second take");
        payload.Should().Contain("first take", "the previous text travels with the edit");
    }

    [Fact]
    public async Task UnsubscribedEventShouldNotEnqueue()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Fan-in unsubscribed" });
        var hook = await CreateHook(Alice, WebHookScope.Chat, chatId.Value, WebHookEvents.Reactions);
        // Both hooks ride the same event, so once the canary has its row the other one would have had its too
        var canary = await CreateHook(Alice, WebHookScope.Chat, chatId.Value, WebHookEvents.Messages);

        // act
        await Alice.CreateTextEntry(chatId, "no reaction here");

        // assert
        await WaitForDeliveries(canary.Id, 1);
        (await Backend.ListDeliveries(hook.Id, Constants.WebHooks.DeliveryListLimit, default))
            .Should().BeEmpty("the hook doesn't subscribe to message events");
    }

    [Fact]
    public async Task PlaceAllowListShouldFilterChats()
    {
        // arrange
        var place = await Alice.CreatePlace(true, "Fan-in place");
        var (chatAId, _) = await Alice.CreateChat(true, "Fan-in A", place.Id);
        var (chatBId, _) = await Alice.CreateChat(true, "Fan-in B", place.Id);
        var hook = await CreateHook(
            Alice, WebHookScope.Place, place.Id.Value, WebHookEvents.Messages,
            diff => diff with { ChatIds = ApiArray.New(chatAId) });

        // act
        await Alice.CreateTextEntry(chatBId, "not for the hook");
        await Alice.CreateTextEntry(chatAId, "for the hook");

        // assert
        var deliveries = await WaitForDeliveries(hook.Id, 1);
        var payload = await ReadPayload(deliveries.Single().Id);
        payload.Should().Contain("for the hook");
        payload.Should().NotContain("not for the hook");
        payload.Should().Contain(chatAId.Value);
    }

    [Fact]
    public async Task PersonalSelectedChatShouldRequireRead()
    {
        // arrange
        var (chatId, inviteId) = await Alice.CreateChat(x => x with { Title = "Fan-in personal", IsPublic = true });
        var bob = await Bob.GetOwnAccount();
        var canary = await CreateHook(Alice, WebHookScope.Chat, chatId.Value, WebHookEvents.Messages);
        // The first event runs the fan-in (and its "any personal hooks?" gate) before Bob's hook exists
        await Alice.CreateTextEntry(chatId, "before hook");
        await WaitForDeliveries(canary.Id, 1);
        var hook = await CreateHook(
            Bob, WebHookScope.User, bob.Id.Value, WebHookEvents.Messages,
            diff => diff with { ChatIds = ApiArray.New(chatId) });

        // act - Bob is not a member yet
        await Alice.CreateTextEntry(chatId, "before bob");

        // assert
        await WaitForDeliveries(canary.Id, 2);
        (await Backend.ListDeliveries(hook.Id, Constants.WebHooks.DeliveryListLimit, default))
            .Should().BeEmpty("Bob cannot read the chat yet");

        // act - Bob joins
        await Bob.JoinChat(chatId, inviteId);
        await ComputedTest.When(async ct => {
            var userIds = await AppHost.Services.GetRequiredService<IAuthorsBackend>().ListUserIds(chatId, ct);
            userIds.Should().Contain(bob.Id);
        });
        await Alice.CreateTextEntry(chatId, "after bob");

        // assert - the hook created after the first event is picked up, so the gate was invalidated
        var deliveries = await WaitForDeliveries(hook.Id, 1);
        var payload = await ReadPayload(deliveries.Single().Id);
        payload.Should().Contain("after bob");
    }

    [Fact]
    public async Task DuplicateEventShouldNotDuplicateRow()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Fan-in duplicate" });
        var hook = await CreateHook(Alice, WebHookScope.Chat, chatId.Value, WebHookEvents.Messages);
        var deliveryId = WebHookPayloads.DeliveryId(hook.Id, "message.posted", "1:1");

        // act
        await Commander.Call(new WebHooksBackend_Enqueue(hook.Id, chatId.Value, deliveryId, "message.posted", "{}"));
        await Commander.Call(new WebHooksBackend_Enqueue(hook.Id, chatId.Value, deliveryId, "message.posted", "{}"));

        // assert
        var deliveries = await WaitForDeliveries(hook.Id, 1);
        deliveries.Single().Id.Should().Be(deliveryId);
    }

    // Private methods

    private async Task<WebHook> CreateHook(
        WebClientTester tester,
        WebHookScope scope,
        string scopeId,
        WebHookEvents events,
        Func<WebHookDiff, WebHookDiff>? configure = null)
    {
        var account = await tester.GetOwnAccount();
        var diff = new WebHookDiff { Name = "CI", Url = HookUrl, Events = events };
        diff = configure?.Invoke(diff) ?? diff;
        var hook = (await Commander.Call(new WebHooksBackend_Change(
            scope, scopeId, null, null, Change.Create(diff), account.Id))).WebHook!;
        await ComputedTest.When(async ct
            => (await Backend.ListByScope(scope, scopeId, ct)).Should().Contain(x => x.Id == hook.Id));
        _createdHooks.Add(hook);
        return hook;
    }

    private Task<ApiArray<WebHookDelivery>> WaitForDeliveries(WebHookId hookId, int count)
        => ComputedTest.When(async ct => {
            var deliveries = await Backend.ListDeliveries(hookId, Constants.WebHooks.DeliveryListLimit, ct);
            deliveries.Should().HaveCount(count);
            return deliveries;
        }, TimeSpan.FromSeconds(10));

    private static string EnvelopeId(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<string> ReadPayload(string deliveryId)
    {
        var dbHub = AppHost.Services.DbHub<ChatDbContext>();
        await using var dbContext = await dbHub.CreateDbContext();
        var dbDelivery = await dbContext.WebHookDeliveries.FirstAsync(x => x.Id == deliveryId);
        return dbDelivery.Payload;
    }
}
