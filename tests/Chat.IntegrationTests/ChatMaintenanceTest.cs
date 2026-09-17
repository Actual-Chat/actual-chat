using ActualChat.Testing.Host;
using ActualLab.Generators;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class ChatMaintenanceTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Admin => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebClientTester Owner => field ??= fixture.AppHost.NewWebClientTester(Out);

    protected override async Task DisposeAsync()
    {
        await Admin.DisposeSilentlyAsync();
        await Owner.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task OnlyAdminsShouldToggleMaintenance()
    {
        // arrange
        await Admin.SignInAsUniqueBobAdmin();
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var command = new Chats_SetMaintenance { Session = Owner.Session, ChatId = chatId, IsEnabled = true };

        // act, assert
        await FluentActions.Awaiting(() => Owner.Commander.Call(command)).Should().ThrowAsync<Exception>();
        await SetMode(chatId, true);
        await FluentActions.Awaiting(() => Owner.Commander.Call(command with {
            Uuid = ApiCommand.NewUuid(), IsEnabled = false,
        })).Should().ThrowAsync<Exception>();
        await SetMode(chatId, false);
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
        await SetMode(chatId, true);
        await SetMode(place.Id.RootChatId, false);

        // assert
        await WaitMode(chatId, MaintenanceMode.System);
        await WaitMode(thread.Id, MaintenanceMode.System);
        await SetMode(chatId, false);
        await WaitMode(thread.Id, MaintenanceMode.None);
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
        var unchanged = await Computed.Capture(() => backend.Get(second, default));

        // act
        await Admin.Commander.Call(new MaintenancesBackend_Set(first, MaintenanceMode.System));
        await ComputedTest.When(async ct => {
            (await backend.Get(first, ct)).Should().Be(MaintenanceMode.System);
        });

        // assert
        unchanged.IsConsistent().Should().BeTrue();
        unchanged.Value.Should().Be(MaintenanceMode.None);
        await Admin.Commander.Call(new MaintenancesBackend_Set(first, MaintenanceMode.None));
    }

    [Fact]
    public async Task MaintenanceShouldStopAnExistingClientStream()
    {
        await Admin.SignInAsUniqueBobAdmin();
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var maintenances = Admin.AppServices.GetRequiredService<IMaintenancesBackend>();
        var received = new List<int>();
        await FluentActions.Awaiting(async () => {
            await foreach (var item in Frames().RequireAvailable(maintenances, chatId, default))
                received.Add(item);
        }).Should().ThrowAsync<Exception>();
        received.Should().Equal(1);
        await SetMode(chatId, false);
        return;

        async IAsyncEnumerable<int> Frames()
        {
            yield return 1;
            await SetMode(chatId, true);
            yield return 2;
        }
    }

    // Private methods

    private async Task SetMode(ChatId chatId, bool isEnabled)
    {
        await Admin.Commander.Call(new Chats_SetMaintenance {
            Session = Admin.Session, ChatId = chatId, IsEnabled = isEnabled,
        });
        await WaitMode(chatId, isEnabled ? MaintenanceMode.System : MaintenanceMode.None);
    }

    private Task WaitMode(ChatId chatId, MaintenanceMode expected)
        => ComputedTest.When(async ct => {
            var chat = await Owner.Chats.Get(Owner.Session, chatId, ct);
            chat.Should().NotBeNull();
            chat!.MaintenanceMode.Should().Be(expected);
        }, TimeSpan.FromSeconds(5));
}
