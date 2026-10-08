using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class ChatMaintenanceTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string TestOwnerId = "chat-maintenance-test";

    private WebClientTester Admin => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Owner => field ??= fixture.AppHost.NewWebClientTester(Out);

    protected override async Task DisposeAsync()
    {
        await Admin.DisposeSilentlyAsync();
        await Owner.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task MaintenanceShouldBlockClientMutationsAndRestoreAccess()
    {
        // arrange
        await Admin.SignInAsUniqueBobAdmin();
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "before maintenance");
        var (otherChatId, _) = await Owner.CreateChat(true);

        // act
        await SetMode(chatId, true);

        // assert
        var chat = (await Owner.Chats.Get(Owner.Session, chatId, default))!;
        chat.Rules.CanRead().Should().BeTrue();
        chat.Rules.IsOwner().Should().BeTrue();
        chat.Rules.CanWrite().Should().BeFalse();
        chat.Rules.Has(ChatPermissions.WriteAudio).Should().BeFalse();
        chat.Rules.Has(ChatPermissions.WriteVideo).Should().BeFalse();
        await FluentActions.Awaiting(() => Owner.CreateTextEntry(chatId, "blocked")).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => Owner.UpdateTextEntry(entry.Id, "blocked")).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => Owner.RemoveTextEntry(entry.Id)).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => Owner.Commander.Call(new Chats_RestoreEntry {
            Session = Owner.Session, ChatId = chatId, LocalId = entry.LocalId,
        })).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => Owner.Commander.Call(new Chats_SetPinned {
            Session = Owner.Session, EntryId = entry.Id, MustPin = true,
        })).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => Owner.Commander.Call(new Reactions_React {
            Session = Owner.Session,
            Reaction = new Reaction { Id = default, AuthorId = null!, EntryId = entry.Id, Emoji = Emojis.Lol },
        })).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => Owner.Commander.Call(new ChatThreads_Start {
            Session = Owner.Session, ParentChatId = chatId, Title = "blocked", Description = "", EntryIds = [entry.Id],
        })).Should().ThrowAsync<Exception>();
        var backendEntry = await Admin.Commander.Call(new ChatsBackend_ChangeEntry(
            entry.Id, null, Change.Update(new ChatEntryDiff { Content = "trusted backend update" })));
        backendEntry.Content.Should().Be("trusted backend update");
        await Owner.CreateTextEntry(otherChatId, "unaffected");
        await SetMode(chatId, false);
        await Owner.CreateTextEntry(chatId, "available again");
    }

    [Fact]
    public async Task PlaceMaintenanceShouldReachChildrenAndThreads()
    {
        // arrange
        await Admin.SignInAsUniqueBobAdmin();
        await Owner.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(true);
        var (chatId, _) = await Owner.CreateChat(true, placeId: place.Id);
        var entry = await Owner.CreateTextEntry(chatId, "thread source");
        var thread = await Owner.Commander.Call(new ChatThreads_Start {
            Session = Owner.Session, ParentChatId = chatId, Title = "thread", Description = "", EntryIds = [entry.Id],
        });

        // act
        await SetMode(place.Id.RootChatId, true);

        // assert
        await WaitMode(chatId, MaintenanceMode.System);
        await WaitMode(thread.Id, MaintenanceMode.System);
        await SetMode(place.Id.RootChatId, false);
        await WaitMode(chatId, MaintenanceMode.None);
        await WaitMode(thread.Id, MaintenanceMode.None);
    }

    [Fact]
    public async Task PlaceChatMaintenanceShouldTargetOnlyItsChats()
    {
        // arrange
        await Admin.SignInAsUniqueBobAdmin();
        await Owner.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(true);
        var (first, _) = await Owner.CreateChat(true, placeId: place.Id);
        var (second, _) = await Owner.CreateChat(true, placeId: place.Id);
        var (third, _) = await Owner.CreateChat(true, placeId: place.Id);

        // act
        await SetMode(first, true);
        await SetMode(second, true);
        await SetMode(first, false);

        // assert
        await WaitMode(second, MaintenanceMode.System);
        await WaitMode(first, MaintenanceMode.None);
        await WaitMode(third, MaintenanceMode.None);
        await WaitMode(place.Id.RootChatId, MaintenanceMode.None);
        await SetMode(second, false);
    }

    [Fact]
    public async Task PartitionWritesShouldNotInvalidateUnchangedObjects()
    {
        // arrange
        var services = Admin.AppServices;
        var backend = services.GetRequiredService<IMaintenancesBackend>();
        var fullPartitionKey = new ShardKey(0xa1234567).Head(MaintenanceKey.FullPartitionKeySize);
        var first = new MaintenanceKey($"test:{RandomStringGenerator.Default.Next()}", fullPartitionKey);
        var second = new MaintenanceKey($"test:{RandomStringGenerator.Default.Next()}", fullPartitionKey);
        first.PartitionKey.Should().Be(second.PartitionKey);
        var unchanged = await Computed.Capture(() => backend.GetMode(second, default));

        // act
        await Admin.Commander.Call(new MaintenancesBackend_Set(first, MaintenanceMode.System));
        await TestWait.When(async ct => {
            (await backend.GetMode(first, ct)).Should().Be(MaintenanceMode.System);
        });

        // assert
        unchanged.IsConsistent().Should().BeTrue();
        unchanged.Value.Should().Be(MaintenanceMode.None);
        await Admin.Commander.Call(new MaintenancesBackend_Set(first, MaintenanceMode.None));
    }

    [Fact]
    public async Task ConcurrentlyAddedTargetsShouldAllBeKept()
    {
        // arrange
        var backend = Admin.AppServices.GetRequiredService<IMaintenancesBackend>();
        var key = MaintenanceKey.New($"test:{RandomStringGenerator.Default.Next()}");
        var targets = Enumerable.Range(0, 8).Select(i => $"target-{i}").ToArray();
        var addTargetCmds = targets.Select(target => new MaintenancesBackend_Set(key, MaintenanceMode.System) {
            OwnerId = TestOwnerId, TargetDiff = new([target]),
        });

        // act
        await Task.WhenAll(addTargetCmds.Select(command => Admin.Commander.Call(command)));

        // assert
        await TestWait.When(async ct => {
            var maintenance = await backend.Get(key, ct);
            maintenance.Mode.Should().Be(MaintenanceMode.System);
            maintenance.Targets.Should().BeEquivalentTo(targets, "no concurrent change may overwrite another");
        });
        var endCmd = new MaintenancesBackend_Set(key, MaintenanceMode.None) { OwnerId = TestOwnerId };
        await Admin.Commander.Call(endCmd);
    }

    [Fact]
    public async Task RemovingTheLastTargetShouldEndMaintenance()
    {
        // arrange
        var backend = Admin.AppServices.GetRequiredService<IMaintenancesBackend>();
        var key = MaintenanceKey.New($"test:{RandomStringGenerator.Default.Next()}");
        var startCmd = new MaintenancesBackend_Set(key, MaintenanceMode.Removal) {
            OwnerId = TestOwnerId, TargetDiff = new(["first", "second"]),
        };
        await Admin.Commander.Call(startCmd);
        var removeFirstCmd = new MaintenancesBackend_Set(key, MaintenanceMode.None) {
            OwnerId = TestOwnerId, TargetDiff = new([], ["first"]),
        };
        await Admin.Commander.Call(removeFirstCmd);
        await TestWait.When(async ct => {
            var maintenance = await backend.Get(key, ct);
            maintenance.Mode.Should().Be(MaintenanceMode.Removal, "a target diff keeps the stored mode");
            maintenance.Targets.Should().Equal("second");
        });

        // act
        var removeSecondCmd = removeFirstCmd with { TargetDiff = new([], ["second"]) };
        await Admin.Commander.Call(removeSecondCmd);

        // assert
        await TestWait.When(async ct => (await backend.Get(key, ct)).Should().Be(Maintenance.None));
        await Admin.Commander.Call(removeSecondCmd);
        var maintenance = await backend.Get(key, default);
        maintenance.Should().Be(Maintenance.None, "removing a target from no row is a no-op");
    }

    [Fact]
    public async Task TargetChangesShouldLeaveAWholeKeyMaintenanceAsItIs()
    {
        // arrange
        var backend = Admin.AppServices.GetRequiredService<IMaintenancesBackend>();
        var key = MaintenanceKey.New($"test:{RandomStringGenerator.Default.Next()}");
        var startCmd = new MaintenancesBackend_Set(key, MaintenanceMode.Removal) { OwnerId = TestOwnerId };
        await Admin.Commander.Call(startCmd);
        await TestWait.When(async ct => (await backend.GetMode(key, ct)).Should().Be(MaintenanceMode.Removal));

        // act
        var addTargetCmd = startCmd with { TargetDiff = new(["added"]) };
        await Admin.Commander.Call(addTargetCmd);
        var removeTargetCmd = startCmd with { TargetDiff = new([], ["removed"]) };
        await Admin.Commander.Call(removeTargetCmd);

        // assert
        var maintenance = await backend.Get(key, default);
        maintenance.Mode.Should().Be(MaintenanceMode.Removal);
        maintenance.Targets.Should().BeEmpty("a maintenance covering the whole key already covers every target");
        var endCmd = new MaintenancesBackend_Set(key, MaintenanceMode.None) { OwnerId = TestOwnerId };
        await Admin.Commander.Call(endCmd);
    }

    // Private methods

    private async Task SetMode(ChatId chatId, bool isEnabled)
    {
        await Admin.SetChatMaintenance(chatId, isEnabled);
        await WaitMode(chatId, isEnabled ? MaintenanceMode.System : MaintenanceMode.None);
    }

    private Task WaitMode(ChatId chatId, MaintenanceMode expected)
        => TestWait.When(async ct => {
            var chat = await Owner.Chats.Get(Owner.Session, chatId, ct);
            chat.Should().NotBeNull();
            chat!.MaintenanceMode.Should().Be(expected);
        }, TimeSpan.FromSeconds(5));
}
