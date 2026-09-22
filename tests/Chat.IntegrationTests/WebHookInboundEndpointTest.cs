using System.Net;
using System.Net.Http.Json;
using System.Text;
using ActualChat.Resilience;
using ActualChat.Testing.Host;
using ActualChat.WebHooks;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class WebHookInboundEndpointTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Alice => field ??= fixture.AppHost.NewWebClientTester(Out);
    private IChatsBackend ChatsBackend => field ??= AppHost.Services.GetRequiredService<IChatsBackend>();

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
    public async Task JsonPostShouldCreateEntryUnderTheBot()
    {
        // arrange
        var (chatId, token) = await NewHook("Alerts");
        using var http = AppHost.NewHttpClient();

        // act
        var response = await http.PostAsJsonAsync($"/hooks/in/{token}", new { text = "Deploy **done**" });
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!["ok"].ToString().Should().Be("True");
        var localId = long.Parse(body["id"].ToString()!);
        var entry = await ChatsBackend.GetEntry(ChatEntryId.New(chatId, localId), default);
        entry!.Content.Should().Be("Deploy **done**");
        entry.AuthorId.LocalId.Should().BeNegative("an incoming hook posts as its bot author");
    }

    [Fact]
    public async Task FormPostShouldWorkLikeJson()
    {
        // arrange
        var (chatId, token) = await NewHook("Form");
        using var http = AppHost.NewHttpClient();
        var content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("payload", """{"text":"from form"}"""),
        ]);

        // act
        var response = await http.PostAsync($"/hooks/in/{token}", content);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var localId = long.Parse(body!["id"].ToString()!);
        var entry = await ChatsBackend.GetEntry(ChatEntryId.New(chatId, localId), default);
        entry!.Content.Should().Be("from form");
    }

    [Fact]
    public async Task ReplyToShouldLinkTheEntries()
    {
        // arrange
        var (chatId, token) = await NewHook("Reply");
        using var http = AppHost.NewHttpClient();
        var firstResponse = await http.PostAsJsonAsync($"/hooks/in/{token}", new { text = "first" });
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var firstLocalId = long.Parse(firstBody!["id"].ToString()!);

        // act
        var response = await http.PostAsJsonAsync($"/hooks/in/{token}", new { text = "second", replyTo = firstLocalId });
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var localId = long.Parse(body!["id"].ToString()!);
        var entry = await ChatsBackend.GetEntry(ChatEntryId.New(chatId, localId), default);
        entry!.Content.Should().Be("second");
        entry.RepliedEntryLid.Should().Be(firstLocalId);
    }

    [Theory]
    [InlineData(true)] // Malformed: rejected before the lookup
    [InlineData(false)] // Well-formed but never minted: rejected by the lookup
    public async Task UnknownTokenShouldBe404WithoutHint(bool isMalformed)
    {
        // arrange
        using var http = AppHost.NewHttpClient();
        var token = isMalformed ? "whin_nope" : WebHookTokens.New();

        // act
        var response = await http.PostAsJsonAsync($"/hooks/in/{token}", new { text = "x" });

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync())
            .Should().NotContain("hook", "a 404 must not reveal that web hooks exist");
    }

    [Fact]
    public async Task DisabledHookShouldBe410()
    {
        // arrange
        var (chatId, token, hook) = await NewHookWithModel("Off");
        var alice = await Alice.GetOwnAccount();
        await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, hook.Id, hook.Version,
            Change.Update(new WebHookDiff { IsEnabled = false }), alice.Id));
        using var http = AppHost.NewHttpClient();

        // act
        var response = await http.PostAsJsonAsync($"/hooks/in/{token}", new { text = "x" });

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task ArchivedChatShouldBe410()
    {
        // arrange
        var (chatId, token) = await NewHook("Archived");
        await Alice.Commander.Call(new Chats_Change {
            Session = Alice.Session,
            ChatId = chatId,
            ExpectedVersion = null,
            Change = Change.Update(new ChatDiff { IsArchived = true }),
        });
        using var http = AppHost.NewHttpClient();

        // act
        var response = await http.PostAsJsonAsync($"/hooks/in/{token}", new { text = "x" });

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Theory]
    [InlineData("""{}""", HttpStatusCode.BadRequest)]
    [InlineData("""{"text":""}""", HttpStatusCode.BadRequest)]
    [InlineData("""{"text":"x","replyTo":999999}""", HttpStatusCode.BadRequest)]
    [InlineData("not json", HttpStatusCode.BadRequest)]
    public async Task BadBodiesShouldBe400(string body, HttpStatusCode expected)
    {
        // arrange
        var (_, token) = await NewHook("Bad");
        using var http = AppHost.NewHttpClient();

        // act
        var response = await http.PostAsync(
            $"/hooks/in/{token}",
            new StringContent(body, Encoding.UTF8, "application/json"));

        // assert
        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task OversizedBodyShouldBe413()
    {
        // arrange
        var (_, token) = await NewHook("Big");
        using var http = AppHost.NewHttpClient();
        var text = new string('x', Constants.WebHooks.InboundBodyLimit + 1);

        // act
        var response = await http.PostAsync(
            $"/hooks/in/{token}",
            new StringContent($$"""{"text":"{{text}}"}""", Encoding.UTF8, "application/json"));

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task PostsOverTheLimitShouldBe429WithRetryAfter()
    {
        // arrange
        var (_, token) = await NewHook("Flood");
        using var http = AppHost.NewHttpClient();
        var policy = AppHost.Services.GetRequiredService<RateLimitPolicy>();
        policy.IsCharged(RateLimitClass.WebHookInbound, RateLimitIdentityKind.Target)
            .Should().BeTrue("the test host must have the real rate limit rules, not RateLimitPolicy.Unlimited");
        var budget = RateLimitBudgets.Default.Get(RateLimitClass.WebHookInbound, RateLimitIdentityKind.Target)!;

        // act
        // Rejected posts are charged too, so an empty body is enough to spend the budget
        HttpResponseMessage? response = null;
        var acceptedCount = 0;
        for (var i = 0; i < budget.Limit + 1; i++) {
            response?.Dispose();
            response = await http.PostAsync($"/hooks/in/{token}",
                new StringContent("{}", Encoding.UTF8, "application/json"));
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                break;

            acceptedCount++;
        }

        // assert
        response!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            "the post after the budget of {0} is over the per-hook limit", budget.Limit);
        acceptedCount.Should().Be((int)budget.Limit, "the budget must be spent in full before anything is refused");
        response.Headers.RetryAfter.Should().NotBeNull();
        response.Dispose();
    }

    [Fact]
    public async Task CardImageShouldBecomeAttachment()
    {
        // arrange
        var (chatId, token) = await NewHook("Img");
        await using var images = new WebHookReceiver { ServeImage = true };
        using var http = AppHost.NewHttpClient();
        var imageUrl = new Uri(images.BaseUri, "chart.png").ToString();

        // act
        var response = await http.PostAsJsonAsync($"/hooks/in/{token}", new {
            text = "chart",
            attachments = new[] { new { image_url = imageUrl } },
        });
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        // assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var localId = long.Parse(body!["id"].ToString()!);
        var entry = await ChatsBackend.GetEntry(ChatEntryId.New(chatId, localId), default);
        entry!.Attachments.Should().ContainSingle();
    }

    [Fact]
    public async Task PostedEntryShouldReachOutgoingHooksWithWebhookOrigin()
    {
        // arrange
        var (chatId, token, incoming) = await NewHookWithModel("Origin");
        var alice = await Alice.GetOwnAccount();
        await using var receiver = new WebHookReceiver();
        var diff = new WebHookDiff { Name = "Out", Url = receiver.HookUrl, Events = WebHookEvents.MessagePosted };
        await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null, Change.Create(diff), alice.Id));
        using var http = AppHost.NewHttpClient();

        // act
        await http.PostAsJsonAsync($"/hooks/in/{token}", new { text = "hello" });
        var delivery = await receiver.Next(TimeSpan.FromSeconds(20));

        // assert
        delivery.Body.Should().Contain("\"kind\":\"webhook\"");
        delivery.Body.Should().Contain($"\"webHookId\":\"{incoming.Id}\"");
    }

    // Private methods

    private async Task<(ChatId ChatId, string Token)> NewHook(string name)
    {
        var (chatId, token, _) = await NewHookWithModel(name);
        return (chatId, token);
    }

    private async Task<(ChatId ChatId, string Token, WebHook Hook)> NewHookWithModel(string name)
    {
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = name });
        var alice = await Alice.GetOwnAccount();
        var result = await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(new WebHookDiff { Name = name, Kind = WebHookKind.Incoming }), alice.Id));
        return (chatId, result.Secret!, result.WebHook!);
    }
}
