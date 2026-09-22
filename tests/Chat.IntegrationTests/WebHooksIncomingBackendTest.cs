using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualChat.WebHooks;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class WebHooksIncomingBackendTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Alice => field ??= fixture.AppHost.NewWebClientTester(Out);
    private IWebHooksBackend Backend => field ??= AppHost.Services.GetRequiredService<IWebHooksBackend>();
    private IAuthorsBackend AuthorsBackend => field ??= AppHost.Services.GetRequiredService<IAuthorsBackend>();
    private IAccountsBackend AccountsBackend => field ??= AppHost.Services.GetRequiredService<IAccountsBackend>();

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
    public async Task CreateShouldMintTokenBotAccountAndAuthor()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Inbound" });
        var alice = await Alice.GetOwnAccount();

        // act
        var result = await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(new WebHookDiff { Name = "Alerts", Kind = WebHookKind.Incoming, DisplayName = "Alerts bot" }),
            alice.Id));

        // assert
        var hook = result.WebHook!;
        hook.Kind.Should().Be(WebHookKind.Incoming);
        WebHookTokens.LooksValid(result.Secret!).Should().BeTrue();
        var byHash = await Backend.GetByTokenHash(WebHookTokens.Hash(result.Secret!), default);
        byHash!.Id.Should().Be(hook.Id);
        var botUserId = hook.Id.ToBotUserId();
        var account = await AccountsBackend.Get(botUserId, default);
        account!.IsBot.Should().BeTrue();
        account.Avatar.Name.Should().Be("Alerts bot");
        var author = await AuthorsBackend.GetByUserId(chatId, botUserId, RequestedAuthorKind.Full, default);
        author!.Id.LocalId.Should().BeNegative();
        author.HasLeft.Should().BeFalse();
    }

    [Fact]
    public async Task RotateShouldReplaceTokenWithoutOverlap()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Rotate" });
        var alice = await Alice.GetOwnAccount();
        var created = await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(new WebHookDiff { Name = "R", Kind = WebHookKind.Incoming }), alice.Id));
        var oldToken = created.Secret!;
        var hook = created.WebHook!;
        (await Backend.GetByTokenHash(WebHookTokens.Hash(oldToken), default))!.Id.Should().Be(hook.Id,
            "the lookup must be cached before the rotation, so the assertions below test invalidation");

        // act
        var newToken = await Commander.Call(new WebHooksBackend_RotateSecret(hook.Id, chatId.Value));

        // assert
        newToken.Should().NotBe(oldToken);
        await ComputedTest.When(async ct => {
            (await Backend.GetByTokenHash(WebHookTokens.Hash(oldToken), ct)).Should().BeNull();
            (await Backend.GetByTokenHash(WebHookTokens.Hash(newToken), ct))!.Id.Should().Be(hook.Id);
        });
    }

    [Fact]
    public async Task UpdateShouldRenameBotAndDeleteShouldRetireIt()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Retire" });
        var alice = await Alice.GetOwnAccount();
        var created = await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(new WebHookDiff { Name = "Old", Kind = WebHookKind.Incoming, DisplayName = "Old bot" }),
            alice.Id));
        var hook = created.WebHook!;
        var botUserId = hook.Id.ToBotUserId();
        (await Backend.GetByTokenHash(WebHookTokens.Hash(created.Secret!), default))!.Id.Should().Be(hook.Id,
            "the lookup must be cached before the delete, so the assertions below test invalidation");

        // act
        var updated = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, hook.Id, hook.Version,
            Change.Update(new WebHookDiff { DisplayName = "New bot" }), alice.Id))).WebHook!;
        await ComputedTest.When(async ct
            => (await AccountsBackend.Get(botUserId, ct))!.Avatar.Name.Should().Be("New bot"));
        await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, hook.Id, updated.Version,
            Change.Remove<WebHookDiff>(), alice.Id));

        // assert
        await ComputedTest.When(async ct => {
            (await Backend.Get(hook.Id, ct)).Should().BeNull();
            (await Backend.GetByTokenHash(WebHookTokens.Hash(created.Secret!), ct)).Should().BeNull();
            var author = await AuthorsBackend.GetByUserId(chatId, botUserId, RequestedAuthorKind.Full, ct);
            author!.HasLeft.Should().BeTrue();
            (await AccountsBackend.Get(botUserId, ct))!.Status.Should().Be(AccountStatus.Suspended);
        });
    }

    [Fact]
    public async Task CreateShouldReuseTheSuppliedIdAndRejectADuplicate()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Id" });
        var alice = await Alice.GetOwnAccount();
        var id = WebHookId.New();
        var create = () => Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, id, null,
            Change.Create(new WebHookDiff { Name = "Once", Kind = WebHookKind.Incoming }), alice.Id));

        // act
        var created = await create();

        // assert
        created.WebHook!.Id.Should().Be(id, "a retry must reuse the id instead of minting a second bot");
        await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
        (await Backend.ListByScope(WebHookScope.Chat, chatId.Value, default)).Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateShouldRejectAKindChange()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Kind" });
        var alice = await Alice.GetOwnAccount();
        var hook = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(new WebHookDiff { Name = "K", Kind = WebHookKind.Incoming }), alice.Id))).WebHook!;

        // act
        var switchKind = () => Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, hook.Id, hook.Version,
            Change.Update(new WebHookDiff { Kind = WebHookKind.Outgoing }), alice.Id));

        // assert
        await switchKind.Should().ThrowAsync<InvalidOperationException>().WithMessage("*kind*");
        (await Backend.Get(hook.Id, default))!.Kind.Should().Be(WebHookKind.Incoming);
    }

    [Fact]
    public async Task CreateShouldRejectOutgoingFieldsOnAnIncomingHook()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Reject" });
        var alice = await Alice.GetOwnAccount();

        WebHookDiff Incoming(Func<WebHookDiff, WebHookDiff> patch)
            => patch(new WebHookDiff { Name = "I", Kind = WebHookKind.Incoming });

        Func<Task<WebHookChangeResult>> Create(WebHookDiff diff)
            => () => Commander.Call(new WebHooksBackend_Change(
                WebHookScope.Chat, chatId.Value, null, null, Change.Create(diff), alice.Id));

        // act
        var withUrl = Create(Incoming(x => x with { Url = "https://example.com/hook" }));
        var withEvents = Create(Incoming(x => x with { Events = WebHookEvents.Messages }));
        var withChatIds = Create(Incoming(x => x with { ChatIds = ApiArray.New(chatId) }));
        var withNotifications = Create(Incoming(x => x with { SubscribeNotifications = true }));
        var withHeader = Create(Incoming(x => x with { CustomHeaderName = "X-Token", CustomHeaderValue = "x" }));
        var inUserScope = () => Commander.Call(new WebHooksBackend_Change(
            WebHookScope.User, alice.Id.Value, null, null,
            Change.Create(new WebHookDiff { Name = "I", Kind = WebHookKind.Incoming }), alice.Id));

        // assert
        await withUrl.Should().ThrowAsync<InvalidOperationException>().WithMessage("*URL*");
        await withEvents.Should().ThrowAsync<InvalidOperationException>().WithMessage("*events*");
        await withChatIds.Should().ThrowAsync<InvalidOperationException>().WithMessage("*own chat*");
        await withNotifications.Should().ThrowAsync<InvalidOperationException>().WithMessage("*notifications*");
        await withHeader.Should().ThrowAsync<InvalidOperationException>().WithMessage("*custom header*");
        await inUserScope.Should().ThrowAsync<InvalidOperationException>().WithMessage("*chat-scoped*");
        (await Backend.ListByScope(WebHookScope.Chat, chatId.Value, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task DisableShouldInvalidateTheTokenLookup()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Disable" });
        var alice = await Alice.GetOwnAccount();
        var created = await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(new WebHookDiff { Name = "D", Kind = WebHookKind.Incoming }), alice.Id));
        var tokenHash = WebHookTokens.Hash(created.Secret!);
        (await Backend.GetByTokenHash(tokenHash, default))!.IsEnabled.Should().BeTrue();

        // act
        await Commander.Call(new WebHooksBackend_Disable(
            created.WebHook!.Id, chatId.Value, WebHookDisabledReason.Manual, null));

        // assert
        await ComputedTest.When(async ct
            => (await Backend.GetByTokenHash(tokenHash, ct))!.IsEnabled.Should().BeFalse());
    }

    [Fact]
    public async Task CreateShouldRejectAClientChosenId()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "NoId" });

        // act
        var createWithId = () => Alice.Commander.Call(new WebHooks_Change {
            Session = Alice.Session,
            Scope = WebHookScope.Chat,
            ScopeId = chatId.Value,
            Id = WebHookId.New(),
            Change = Change.Create(new WebHookDiff { Name = "Chosen", Kind = WebHookKind.Incoming }),
        });

        // assert
        await createWithId.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Id must be empty*");
        (await Backend.ListByScope(WebHookScope.Chat, chatId.Value, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task CreateShouldRejectAnIdPointingAtAnotherAccount()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Hijack" });
        var alice = await Alice.GetOwnAccount();
        // A short hook id lands in the id space of real user ids: whin + 4 chars is a valid user id
        var hookId = WebHookId.Parse(Guid.NewGuid().ToString("N")[..4]);
        var victimId = hookId.ToBotUserId();
        var victim = await InternalAccounts.Create(
            AppHost.Services, new InternalUserInfo(victimId, "victim"), default);
        victim.IsBot.Should().BeFalse();

        // act
        var hijack = () => Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, hookId, null,
            Change.Create(new WebHookDiff { Name = "Hijack", Kind = WebHookKind.Incoming }), alice.Id));

        // assert
        await hijack.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already taken*");
        var account = await AccountsBackend.Get(victimId, default);
        account!.IsBot.Should().BeFalse("an unrelated account must never be turned into a hook bot");
        account.Status.Should().Be(victim.Status);
        (await AuthorsBackend.GetByUserId(chatId, victimId, RequestedAuthorKind.Full, default))
            .Should().BeNull("and must never be joined to the hook's chat");
    }

    [Fact]
    public async Task CreateShouldNotResurrectADeletedHooksBot()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Resurrect" });
        var alice = await Alice.GetOwnAccount();
        var id = WebHookId.New();
        var hook = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, id, null,
            Change.Create(new WebHookDiff { Name = "Gone", Kind = WebHookKind.Incoming }), alice.Id))).WebHook!;
        await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, hook.Id, hook.Version, Change.Remove<WebHookDiff>(), alice.Id));

        // act
        var recreate = () => Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, id, null,
            Change.Create(new WebHookDiff { Name = "Taken", Kind = WebHookKind.Incoming, DisplayName = "Hijack" }),
            alice.Id));

        // assert
        await recreate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already taken*");
        await ComputedTest.When(async ct => {
            var account = await AccountsBackend.Get(id.ToBotUserId(), ct);
            account!.Status.Should().Be(AccountStatus.Suspended, "the deleted hook's bot must stay retired");
            account.Avatar.Name.Should().Be("Gone", "its history in the old chat keeps the original name");
        });
    }

    [Fact]
    public async Task CreateShouldReuseTheBotLeftByAnEarlierAttempt()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Retry" });
        var alice = await Alice.GetOwnAccount();
        var id = WebHookId.New();
        var botUserId = id.ToBotUserId();
        await InternalAccounts.Create(
            AppHost.Services,
            new InternalUserInfo(botUserId, "Retried", AvatarName: "Retried") { IsBot = true },
            default);
        var author = await Commander.Call(new AuthorsBackend_Upsert(
            chatId, null, botUserId, null, new AuthorDiff(), DoNotNotify: true));

        // act
        var created = await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, id, null,
            Change.Create(new WebHookDiff { Name = "Retried", Kind = WebHookKind.Incoming }), alice.Id));

        // assert
        created.WebHook!.Id.Should().Be(id, "an active bot is the same create's earlier attempt");
        var account = await AccountsBackend.Get(botUserId, default);
        account!.Status.Should().Be(AccountStatus.Active);
        account.IsBot.Should().BeTrue();
        var botAuthor = await AuthorsBackend.GetByUserId(chatId, botUserId, RequestedAuthorKind.Full, default);
        botAuthor!.Id.Should().Be(author.Id, "the author upsert is idempotent per user");
        botAuthor.HasLeft.Should().BeFalse();
    }

    [Fact]
    public async Task ExcludeShouldAllowABotAuthorWithoutAHook()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Orphan" });
        var botUserId = WebHookId.New().ToBotUserId();
        await InternalAccounts.Create(
            AppHost.Services,
            new InternalUserInfo(botUserId, "Orphan", AvatarName: "Orphan") { IsBot = true },
            default);
        var author = await Commander.Call(new AuthorsBackend_Upsert(
            chatId, null, botUserId, null, new AuthorDiff(), DoNotNotify: true));

        // act
        await Alice.Commander.Call(new Authors_Exclude { Session = Alice.Session, AuthorId = author.Id });

        // assert
        await ComputedTest.When(async ct
            => (await AuthorsBackend.GetByUserId(chatId, botUserId, RequestedAuthorKind.Full, ct))!
                .HasLeft.Should().BeTrue("a bot whose hook is gone is an ordinary stuck member"));
    }

    [Fact]
    public async Task ExcludeShouldRejectTheBotAuthor()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Exclude" });
        var alice = await Alice.GetOwnAccount();
        var hook = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(new WebHookDiff { Name = "E", Kind = WebHookKind.Incoming }), alice.Id))).WebHook!;
        var author = await AuthorsBackend
            .GetByUserId(chatId, hook.Id.ToBotUserId(), RequestedAuthorKind.Full, default)
            .Require();

        // act
        var exclude = () => Alice.Commander.Call(new Authors_Exclude {
            Session = Alice.Session,
            AuthorId = author.Id,
        });

        // assert
        await exclude.Should().ThrowAsync<InvalidOperationException>().WithMessage("*integration*");
        (await AuthorsBackend.GetByUserId(chatId, hook.Id.ToBotUserId(), RequestedAuthorKind.Full, default))!
            .HasLeft.Should().BeFalse();
    }

    [Fact]
    public async Task RecordPostShouldBumpLastActivity()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Post" });
        var alice = await Alice.GetOwnAccount();
        var hook = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(new WebHookDiff { Name = "P", Kind = WebHookKind.Incoming }), alice.Id))).WebHook!;

        // act
        await Commander.Call(new WebHooksBackend_RecordPost(hook.Id, chatId.Value));

        // assert
        await ComputedTest.When(async ct
            => (await Backend.Get(hook.Id, ct))!.LastActivityAt.Should().NotBeNull());
    }

    [Fact]
    public async Task TestShouldPostAMessageForIncomingHooks()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "TestPost" });
        var alice = await Alice.GetOwnAccount();
        var hook = (await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null,
            Change.Create(new WebHookDiff { Name = "T", Kind = WebHookKind.Incoming, DisplayName = "Tester" }),
            alice.Id))).WebHook!;
        var chatsBackend = AppHost.Services.GetRequiredService<IChatsBackend>();

        // act
        var result = await Commander.Call(new WebHooksBackend_Test(hook.Id, chatId.Value, alice.Avatar.Name));

        // assert
        result.IsSuccess.Should().BeTrue();
        var range = await chatsBackend.GetLidRange(chatId, false, default);
        var last = await chatsBackend.GetEntry(ChatEntryId.New(chatId, range.End - 1), default);
        last!.Content.Should().Be($"Test message from {alice.Avatar.Name}");
        last.AuthorId.LocalId.Should().BeNegative();
    }
}
