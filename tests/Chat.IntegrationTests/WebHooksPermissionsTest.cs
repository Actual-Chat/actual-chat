using ActualChat.Testing.Host;
using ActualChat.WebHooks;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class WebHooksPermissionsTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Alice => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Bob => field ??= fixture.AppHost.NewWebClientTester(Out);

    protected override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await Alice.SignInAsAlice();
        await Bob.SignInAsBob();
    }

    protected override async Task DisposeAsync()
    {
        await Alice.DisposeSilentlyAsync();
        await Bob.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task NonModeratorShouldNotSeeOrCreateChatHooks()
    {
        // arrange
        var aliceWebHooks = Alice.AppServices.GetRequiredService<IWebHooks>();
        var bobWebHooks = Bob.AppServices.GetRequiredService<IWebHooks>();
        var (chatId, inviteId) = await Alice.CreateChat(isPublicChat: true);
        await Bob.JoinChat(chatId, inviteId);
        var createChange = new WebHooks_Change {
            Session = Alice.Session,
            Scope = WebHookScope.Chat,
            ScopeId = chatId.Value,
            Change = Change.Create(new WebHookDiff {
                Name = "Alice hook",
                Url = "https://example.com/hook",
                Events = WebHookEvents.MessagePosted,
            }),
        };

        // act
        var bobList = await Record.ExceptionAsync(
            () => bobWebHooks.List(Bob.Session, WebHookScope.Chat, chatId.Value, CancellationToken.None));
        var bobCreate = await Record.ExceptionAsync(
            () => Bob.Commander.Call(createChange with { Session = Bob.Session }));
        var aliceResult = await Alice.Commander.Call(createChange);
        var aliceList = await aliceWebHooks.List(
            Alice.Session, WebHookScope.Chat, chatId.Value, CancellationToken.None);

        // assert - every scope answers a non-manager the same way
        bobList.Should().BeOfType<UnauthorizedAccessException>();
        bobCreate.Should().BeOfType<UnauthorizedAccessException>();
        aliceResult.WebHook.Should().NotBeNull();
        aliceList.Count.Should().Be(1);
    }

    [Fact]
    public async Task PersonalHooksShouldBeInvisibleToOthers()
    {
        // arrange
        var aliceAccount = await Alice.Accounts.GetOwn(Alice.Session, CancellationToken.None);
        var aliceWebHooks = Alice.AppServices.GetRequiredService<IWebHooks>();
        var bobWebHooks = Bob.AppServices.GetRequiredService<IWebHooks>();
        var createChange = new WebHooks_Change {
            Session = Alice.Session,
            Scope = WebHookScope.User,
            ScopeId = aliceAccount.Id.Value,
            Change = Change.Create(new WebHookDiff {
                Name = "Alice's personal hook",
                Url = "https://example.com/hook",
                Events = WebHookEvents.Notification,
                SubscribeNotifications = true,
            }),
        };

        // act
        var aliceResult = await Alice.Commander.Call(createChange);
        var hookId = aliceResult.WebHook!.Id;
        var bobList = await Record.ExceptionAsync(
            () => bobWebHooks.List(Bob.Session, WebHookScope.User, aliceAccount.Id.Value, CancellationToken.None));
        var bobGet = await bobWebHooks.Get(Bob.Session, hookId, CancellationToken.None);
        var aliceGet = await aliceWebHooks.Get(Alice.Session, hookId, CancellationToken.None);

        // assert
        bobList.Should().BeOfType<UnauthorizedAccessException>();
        bobGet.Should().BeNull();
        aliceGet.Should().NotBeNull();
    }

    [Fact]
    public async Task ListMineShouldReturnOnlyOwnHooksAcrossScopes()
    {
        // arrange
        var aliceAccount = await Alice.Accounts.GetOwn(Alice.Session, CancellationToken.None);
        var aliceWebHooks = Alice.AppServices.GetRequiredService<IWebHooks>();
        var bobWebHooks = Bob.AppServices.GetRequiredService<IWebHooks>();
        var (chatId, _) = await Alice.CreateChat(isPublicChat: true);
        var diff = new WebHookDiff {
            Name = "Mine",
            Url = "https://example.com/hook",
            Events = WebHookEvents.MessagePosted,
        };

        // act
        var chatHook = (await Alice.Commander.Call(new WebHooks_Change {
            Session = Alice.Session,
            Scope = WebHookScope.Chat,
            ScopeId = chatId.Value,
            Change = Change.Create(diff),
        })).WebHook!;
        var userHook = (await Alice.Commander.Call(new WebHooks_Change {
            Session = Alice.Session,
            Scope = WebHookScope.User,
            ScopeId = aliceAccount.Id.Value,
            Change = Change.Create(diff with { Events = WebHookEvents.Notification, SubscribeNotifications = true }),
        })).WebHook!;

        // assert
        await TestWait.When(async ct => {
            var aliceMine = await aliceWebHooks.ListMine(Alice.Session, ct);
            aliceMine.Should().Contain(x => x.Id == chatHook.Id);
            aliceMine.Should().Contain(x => x.Id == userHook.Id);
        });
        var bobMine = await bobWebHooks.ListMine(Bob.Session, CancellationToken.None);
        bobMine.Should().NotContain(x => x.Id == chatHook.Id || x.Id == userHook.Id,
            "ListMine is keyed by creator, not by what the caller can see");
    }

    [Fact]
    public async Task PlaceHookShouldRequireOwner()
    {
        // arrange
        var alicePlace = await Alice.CreatePlace(isPublicPlace: true);
        await Bob.JoinPlace(alicePlace.Id);
        var createChange = new WebHooks_Change {
            Session = Bob.Session,
            Scope = WebHookScope.Place,
            ScopeId = alicePlace.Id.Value,
            Change = Change.Create(new WebHookDiff {
                Name = "Place hook",
                Url = "https://example.com/hook",
                Events = WebHookEvents.PlaceUpdated,
            }),
        };

        // act
        var bobCreate = await Record.ExceptionAsync(() => Bob.Commander.Call(createChange));
        var aliceResult = await Alice.Commander.Call(createChange with { Session = Alice.Session });

        // assert
        bobCreate.Should().BeOfType<UnauthorizedAccessException>();
        aliceResult.WebHook.Should().NotBeNull();
    }

    [Fact]
    public async Task GuestGetShouldReturnNullInsteadOfThrowing()
    {
        // arrange
        var aliceAccount = await Alice.Accounts.GetOwn(Alice.Session, CancellationToken.None);
        var createChange = new WebHooks_Change {
            Session = Alice.Session,
            Scope = WebHookScope.User,
            ScopeId = aliceAccount.Id.Value,
            Change = Change.Create(new WebHookDiff {
                Name = "Alice's personal hook",
                Url = "https://example.com/hook",
                Events = WebHookEvents.Notification,
                SubscribeNotifications = true,
            }),
        };
        var aliceResult = await Alice.Commander.Call(createChange);
        var hookId = aliceResult.WebHook!.Id;
        await using var guest = fixture.AppHost.NewWebClientTester(Out);
        var guestWebHooks = guest.AppServices.GetRequiredService<IWebHooks>();

        // act
        var guestGet = await guestWebHooks.Get(guest.Session, hookId, CancellationToken.None);

        // assert
        guestGet.Should().BeNull();
    }
}
