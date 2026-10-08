using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Flows;
using ActualChat.Testing.Host;
using ActualChat.Users;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class AccountChatRemovalTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string MaintenanceOwnerId = "account-chat-removal-test";

    private WebClientTester Owner => field ??= AppHost.NewWebClientTester(Out);
    private IChatsBackend Backend => field ??= Owner.AppServices.GetRequiredService<IChatsBackend>();
    private IPlacesBackend Places => field ??= Owner.AppServices.GetRequiredService<IPlacesBackend>();
    private IMaintenancesBackend Maintenances => field ??= Owner.AppServices.GetRequiredService<IMaintenancesBackend>();
    private DbHub<ChatDbContext> DbHub => field ??= Owner.AppServices.GetRequiredService<DbHub<ChatDbContext>>();

    protected override async Task DisposeAsync()
    {
        await Owner.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task SoleOwnedChatShouldBeDrainedUnderRemovalMaintenance()
    {
        // arrange
        var alice = await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await Owner.CreateTextEntry(chatId, "owned history");
        var key = chatId.ToMaintenanceKey();
        var maintenance = await Computed.Capture(() => Maintenances.Get(key, default));

        // act
        await Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(alice.Id));

        // assert
        maintenance.IsConsistent().Should().BeFalse();
        (await Owner.Chats.Get(Owner.Session, chatId, default)).Should().BeNull();
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.Chats.AnyAsync(c => c.Id == chatId.Value)).Should().BeFalse();
            (await db.ChatEntries.AnyAsync(e => e.ChatId == chatId.Value)).Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        await TestWait.When(async ct => {
            (await Maintenances.Get(key, ct)).Should().Be(Maintenance.None);
            (await Backend.Get(chatId, ct)).Should().BeNull();
        });
    }

    [Fact]
    public async Task SoleOwnedPlaceShouldGoWithEveryChatInIt()
    {
        // arrange
        var alice = await Owner.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(true);
        var (publicChatId, _) = await Owner.CreateChat(true, placeId: place.Id);
        await Owner.CreateTextEntry(publicChatId, "place history");
        await using var member = AppHost.NewWebClientTester(Out);
        await member.SignInAsUniqueBob();
        await member.JoinPlace(place.Id);
        var (memberChatId, _) = await member.CreateChat(false, placeId: place.Id);
        await member.CreateTextEntry(memberChatId, "member history");
        var key = place.Id.RootChatId.ToMaintenanceKey();
        var maintenance = await Computed.Capture(() => Maintenances.Get(key, default));

        // act
        await Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(alice.Id));

        // assert
        maintenance.IsConsistent().Should().BeFalse();
        (await Owner.Chats.Get(Owner.Session, publicChatId, default)).Should().BeNull();
        (await member.Chats.Get(member.Session, memberChatId, default)).Should().BeNull();
        var placeChatIdPrefix = PlaceChatId.IdPrefix + place.Id.Value;
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.Chats.AnyAsync(c => c.Id.StartsWith(placeChatIdPrefix))).Should().BeFalse();
            (await db.Places.AnyAsync(p => p.Id == place.Id.Value)).Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        await TestWait.When(async ct => {
            (await Places.Get(place.Id, ct)).Should().BeNull();
            (await Maintenances.Get(key, ct)).Should().Be(Maintenance.None);
            (await Backend.Get(publicChatId, ct)).Should().BeNull();
            (await Backend.Get(memberChatId, ct)).Should().BeNull();
        });
    }

    [Fact]
    public async Task PlaceRemovalShouldRemoveOnlyMediaNothingElseCanShare()
    {
        // arrange
        var alice = await Owner.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(true);
        var (chatId, _) = await Owner.CreateChat(true, placeId: place.Id);
        var (systemPictureChatId, _) = await Owner.CreateChat(true, placeId: place.Id);
        var entry = await Owner.CreateTextEntry(chatId, "attachment");
        await using var member = AppHost.NewWebClientTester(Out);
        await member.SignInAsUniqueBob();
        var (memberChatId, _) = await member.CreateChat(true);
        var forwarded = await member.CreateTextEntry(memberChatId, "forwarded attachment");
        var pictureId = MediaId.New(place.Id.Value);
        var backgroundId = MediaId.New(place.Id.Value);
        var chatPictureId = MediaId.New(chatId.Value);
        var attachmentId = MediaId.New(chatId.Value);
        var systemId = MediaId.New(MediaId.SystemIconsScope);
        var media = Owner.AppServices.GetRequiredService<IMediaBackend>();
        foreach (var id in new[] { pictureId, backgroundId, chatPictureId, attachmentId, systemId })
            await Owner.Commander.Call(new MediaBackend_Change(id, null, Change.Create(new MediaFull(id))));
        foreach (var entryId in new[] { entry.Id, forwarded.Id }) {
            var createAttachmentsCmd = new ChatsBackend_CreateAttachments([
                new ChatEntryAttachment { EntryId = entryId, Index = 0, MediaId = attachmentId },
            ]);
            await Owner.Commander.Call(createAttachmentsCmd);
        }
        await using (var db = await DbHub.CreateDbContext(true)) {
            await db.Places.Where(p => p.Id == place.Id.Value).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.MediaId, pictureId.Value)
                .SetProperty(p => p.BackgroundMediaId, backgroundId.Value));
            await db.Chats.Where(c => c.Id == chatId.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.MediaId, chatPictureId.Value));
            await db.Chats.Where(c => c.Id == systemPictureChatId.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.MediaId, systemId.Value));
        }

        // act
        await Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(alice.Id));

        // assert
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.Places.AnyAsync(p => p.Id == place.Id.Value)).Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        await TestWait.When(async ct => {
            (await media.Get(pictureId, ct)).Should().BeNull();
            (await media.Get(backgroundId, ct)).Should().BeNull();
            (await media.Get(chatPictureId, ct)).Should().BeNull();
        });
        (await media.Get(attachmentId, default)).Should().NotBeNull("a forward in another chat still shows it");
        (await media.Get(systemId, default)).Should().NotBeNull("system media is shared by every chat showing it");
        (await member.Chats.Get(member.Session, memberChatId, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task CoOwnedPlaceShouldLoseOnlyPrivateChatsWithNoOtherOwner()
    {
        // arrange
        var alice = await Owner.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(true);
        await using var member = AppHost.NewWebClientTester(Out);
        await member.SignInAsUniqueBob();
        await member.JoinPlace(place.Id);
        var memberAuthor = await member.GetOwnAuthor(place.Id.RootChatId);
        await Owner.PromoteToOwner(memberAuthor.Require().Id);
        var (publicChatId, _) = await Owner.CreateChat(true, placeId: place.Id);
        var (privateChatId, _) = await Owner.CreateChat(false, placeId: place.Id);
        var key = place.Id.RootChatId.ToMaintenanceKey();
        var soleOwnedChatIds = await Backend.ListSoleOwnedChatIds(alice.Id, default);

        // act
        await Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(alice.Id));

        // assert
        soleOwnedChatIds.Should().Contain(privateChatId)
            .And.NotContain(publicChatId)
            .And.NotContain(place.Id.RootChatId);
        (await Owner.Chats.Get(Owner.Session, privateChatId, default)).Should().BeNull();
        (await Backend.IsRemovalPending(publicChatId, default)).Should()
            .BeFalse("the removal maintenance on the Place's root key targets only the private chat");
        (await Owner.Chats.Get(Owner.Session, publicChatId, default)).Should().NotBeNull();
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.Chats.AnyAsync(c => c.Id == privateChatId.Value)).Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        await TestWait.When(async ct => {
            (await Maintenances.Get(key, ct)).Should().Be(Maintenance.None);
            (await Backend.Get(privateChatId, ct)).Should().BeNull();
        });
        (await Backend.Get(publicChatId, default)).Should().NotBeNull();
        (await Places.Get(place.Id, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task CoOwnerWithDeletedAccountShouldNotKeepAChat()
    {
        // arrange
        var alice = await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(false);
        await using var member = AppHost.NewWebClientTester(Out);
        var bob = await member.SignInAsUniqueBob();
        var bobAuthor = await Owner.InviteToChat(chatId, bob.Id);
        await Owner.PromoteToOwner(bobAuthor.Id);
        (await Backend.ListSoleOwnedChatIds(alice.Id, default)).Should().NotContain(chatId);

        // act
        await Owner.Commander.Call(new AccountsBackend_Delete(bob.Id));

        // assert
        // Polled: ListSoleOwnedChatIds isn't a compute method, so nothing invalidates it
        await TestWait.WhenPolled(async () => {
            (await Backend.ListSoleOwnedChatIds(alice.Id, default)).Should().Contain(chatId);
        });
    }

    [Fact]
    public async Task RemovalShouldResumeAChatAFailedAttemptLeftPending()
    {
        // arrange
        var alice = await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await Owner.CreateTextEntry(chatId, "left behind");
        var key = chatId.ToMaintenanceKey();
        var setRemovalCmd = new MaintenancesBackend_Set(key, MaintenanceMode.Removal) {
            OwnerId = ChatsBackend.RemovalMaintenanceOwnerId,
        };
        await Owner.Commander.Call(setRemovalCmd);

        // act
        await Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(alice.Id));

        // assert
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.Chats.AnyAsync(c => c.Id == chatId.Value)).Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        await TestWait.When(async ct => {
            (await Maintenances.Get(key, ct)).Should().Be(Maintenance.None);
        });
    }

    [Fact]
    public async Task RemovalShouldEndTheImportOfTheChatItRemoves()
    {
        // arrange
        var alice = await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await Owner.CreateTextEntry(chatId, "imported history");
        var key = chatId.ToMaintenanceKey();
        var setImportMaintenanceCmd = new MaintenancesBackend_Set(key, MaintenanceMode.Import) {
            OwnerId = MaintenanceOwnerId,
        };
        await Owner.Commander.Call(setImportMaintenanceCmd);

        // act
        await Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(alice.Id));

        // assert
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.Chats.AnyAsync(c => c.Id == chatId.Value)).Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        await TestWait.When(async ct => {
            (await Maintenances.Get(key, ct)).Should().Be(Maintenance.None);
        });
    }

    [Fact]
    public async Task RemovalShouldLeaveSystemMaintenanceToItsOwner()
    {
        // arrange
        var alice = await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await Owner.CreateTextEntry(chatId, "history under maintenance");
        var key = chatId.ToMaintenanceKey();
        var setSystemMaintenanceCmd = new MaintenancesBackend_Set(key, MaintenanceMode.System) {
            OwnerId = MaintenanceOwnerId,
        };
        await Owner.Commander.Call(setSystemMaintenanceCmd);

        // act
        await Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(alice.Id));

        // assert
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));
        await using (var db = await DbHub.CreateDbContext())
            (await db.Chats.AnyAsync(c => c.Id == chatId.Value)).Should()
                .BeTrue("removal can't take over a maintenance another operation owns");
        (await Backend.IsRemovalPending(chatId, default)).Should().BeFalse();
        (await Backend.Get(chatId, default)).Should().NotBeNull();
        var maintenance = await Maintenances.Get(key, default);
        maintenance.Mode.Should().Be(MaintenanceMode.System);
        maintenance.OwnerId.Should().Be(MaintenanceOwnerId);
        var clearMaintenanceCmd = new MaintenancesBackend_Set(key, MaintenanceMode.None) {
            OwnerId = MaintenanceOwnerId,
        };
        await Owner.Commander.Call(clearMaintenanceCmd);
    }

    [Fact]
    public async Task AccountEntriesShouldBePurgedInEveryChatTheAccountWroteTo()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Owner.CreateChat(true);
        var anchor = await Owner.CreateTextEntry(chatId, "alice's anchor");
        var startThreadCmd = new ChatThreads_Start {
            Session = Owner.Session, ParentChatId = chatId, Title = "thread",
            Description = "", EntryIds = [anchor.Id],
        };
        var thread = await Owner.Commander.Call(startThreadCmd);
        var place = await Owner.CreatePlace(true);
        var (placeChatId, _) = await Owner.CreateChat(true, placeId: place.Id);
        await using var member = AppHost.NewWebClientTester(Out);
        var bob = await member.SignInAsUniqueBob();
        await member.JoinChat(chatId, inviteId);
        await member.JoinPlace(place.Id);
        var removed = new List<ChatEntry> {
            await member.CreateTextEntry(chatId, "bob in the chat"),
            await member.CreateTextEntry(thread.Id, "bob in the thread"),
            await member.CreateTextEntry(placeChatId, "bob in the place chat"),
        };
        var kept = new List<ChatEntry> {
            anchor,
            await Owner.CreateTextEntry(chatId, "alice in the chat"),
            await Owner.CreateTextEntry(thread.Id, "alice in the thread"),
            await Owner.CreateTextEntry(placeChatId, "alice in the place chat"),
        };

        // act
        await Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(bob.Id));

        // assert
        await WhenEntriesAndAuthorsPurged(removed, bob.Id);
        foreach (var touchedChatId in new[] { chatId, thread.Id, placeChatId, place.Id.RootChatId })
            await WhenCleanupFlowDrained(touchedChatId);
        foreach (var entry in kept)
            (await Backend.GetEntry(entry.Id, default))!.Content.Should().Be(entry.Content);
    }

    [Fact]
    public async Task ConcurrentAccountEntryRemovalsShouldBothBeProcessed()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Owner.CreateChat(true);
        var kept = await Owner.CreateTextEntry(chatId, "alice stays");
        await using var bobTester = AppHost.NewWebClientTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        await bobTester.JoinChat(chatId, inviteId);
        await using var carolTester = AppHost.NewWebClientTester(Out);
        var carol = await carolTester.SignInAsUniqueBob();
        await carolTester.JoinChat(chatId, inviteId);
        var bobEntries = await bobTester.CreateTextEntries(chatId, "bob goes", 3);
        var carolEntries = await carolTester.CreateTextEntries(chatId, "carol goes", 3);

        // act
        await Task.WhenAll(
            Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(bob.Id)),
            Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(carol.Id)));

        // assert
        await WhenEntriesAndAuthorsPurged(bobEntries, bob.Id);
        await WhenEntriesAndAuthorsPurged(carolEntries, carol.Id);
        await WhenCleanupFlowDrained(chatId);
        (await Backend.GetEntry(kept.Id, default))!.Content.Should().Be("alice stays");
    }

    [Fact]
    public async Task CleanupFlowShouldPurgeMoreRemovedAuthorsThanOneResumeTakes()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var kept = await Owner.CreateTextEntry(chatId, "alice stays");
        var authorIds = Enumerable.Range(0, 12).Select(i => AuthorId.New(chatId, 900_000 + i)).ToList();
        var removed = new List<ChatEntry>();
        foreach (var authorId in authorIds) {
            var createEntryCmd = new ChatsBackend_ChangeEntry(ChatEntryId.New(chatId, 0), null,
                Change.Create(new ChatEntryDiff { AuthorId = authorId, Content = $"by {authorId}" }));
            removed.Add(await Owner.Commander.Call(createEntryCmd));
        }
        var flowId = FlowHub.NewId<ChatPurgeFlow>(chatId.Value);
        var messages = authorIds.Select(id => new ChatPurgeFlow.RemoveAuthorEntries(id));
        var postMessagesCmd = Flows_ChangeInbox.Post(flowId, messages);

        // act
        await Owner.Commander.Call(postMessagesCmd);

        // assert
        await TestWait.WhenPolled(async () => {
            var entrySids = removed.Select(e => e.Id.Value).ToList();
            await using var db = await DbHub.CreateDbContext();
            (await db.ChatEntries.CountAsync(e => entrySids.Contains(e.Id) && !e.IsRemovedAndPurged)).Should().Be(0);
        }, TimeSpan.FromSeconds(30));
        await WhenCleanupFlowDrained(chatId);
        (await Backend.GetEntry(kept.Id, default))!.Content.Should().Be("alice stays");
    }

    [Fact]
    public async Task PlaceRemovalShouldSucceedWhenItsRootChatIsAlreadyGone()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(true);
        var rootChatId = place.Id.RootChatId;
        var removeRootChatCmd = new ChatsBackend_Change(rootChatId, null, Change.Remove<ChatDiff>());
        await Owner.Commander.Call(removeRootChatCmd);
        await TestWait.When(async ct => (await Backend.Get(rootChatId, ct)).Should().BeNull());

        // act
        var removePlaceCmd = new PlacesBackend_Change(place.Id, null, Change.Remove<PlaceDiff>());
        await Owner.Commander.Call(removePlaceCmd);

        // assert
        await TestWait.When(async ct => (await Places.Get(place.Id, ct)).Should().BeNull());
        await using var db = await DbHub.CreateDbContext();
        (await db.Places.AnyAsync(p => p.Id == place.Id.Value)).Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentlyMarkedPlaceChatsShouldBothBeTargeted()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var place = await Owner.CreatePlace(true);
        var (firstChatId, _) = await Owner.CreateChat(true, placeId: place.Id);
        var (secondChatId, _) = await Owner.CreateChat(true, placeId: place.Id);
        var chatSids = new[] { firstChatId.Value, secondChatId.Value };
        var key = place.Id.RootChatId.ToMaintenanceKey();

        // act
        // The chat row locks keep ChatPurgeFlow from draining the chats, which drops their targets
        await using (var lockDb = await DbHub.CreateDbContext(true)) {
            await using var tx = await lockDb.Database.BeginTransactionAsync();
            await lockDb.Chats.ForUpdate().Where(c => chatSids.Contains(c.Id)).ToListAsync();
            await Task.WhenAll(
                Owner.Commander.Call(new ChatsBackend_RequestRemoval(firstChatId)),
                Owner.Commander.Call(new ChatsBackend_RequestRemoval(secondChatId)));

            // assert
            var maintenance = await Maintenances.Get(key, default);
            maintenance.Mode.Should().Be(MaintenanceMode.Removal);
            maintenance.Targets.Should().BeEquivalentTo(
                [firstChatId.ToMaintenanceTarget()!, secondChatId.ToMaintenanceTarget()!],
                "neither concurrent removal may overwrite the other's target");
            await tx.RollbackAsync();
        }
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.Chats.AnyAsync(c => chatSids.Contains(c.Id))).Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        await TestWait.When(async ct => (await Maintenances.Get(key, ct)).Should().Be(Maintenance.None));
        (await Backend.Get(place.Id.RootChatId, default)).Should().NotBeNull();
    }

    // Private methods

    private Task WhenEntriesAndAuthorsPurged(IEnumerable<ChatEntry> entries, UserId userId)
        // Polled: the rows change in the background, and a DB query isn't invalidated by anything
        => TestWait.WhenPolled(async () => {
            var entrySids = entries.Select(e => e.Id.Value).ToList();
            await using var db = await DbHub.CreateDbContext();
            (await db.ChatEntries.AnyAsync(e => entrySids.Contains(e.Id) && !e.IsRemovedAndPurged))
                .Should().BeFalse("every entry of a removed account gets purged");
            (await db.Authors.AnyAsync(a => a.UserId == userId.Value))
                .Should().BeFalse("an author goes once its entries are purged");
        }, TimeSpan.FromSeconds(30));

    private Task WhenCleanupFlowDrained(ChatId chatId)
        // Polled: the flow drains its inbox in the background, and the inbox isn't read through a compute method
        => TestWait.WhenPolled(async () => {
            var flowId = FlowHub.NewId<ChatPurgeFlow>(chatId.Value);
            var flow = await FlowHub.TryGet<ChatPurgeFlow>(flowId);
            flow.Should().NotBeNull($"{chatId} got a message, so its flow has to exist");
            flow!.RemovedAuthorIds.Should().BeEmpty($"{chatId} has purged every removed author");
            (await FlowHub.GetInboxNonComputed(flowId)).Should().BeEmpty();
        }, TimeSpan.FromSeconds(30));
}
