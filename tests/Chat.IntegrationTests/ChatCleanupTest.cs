using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Media;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class ChatCleanupTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private const string ImportOwnerId = "chat-cleanup-test-import";

    private WebClientTester Owner => field ??= AppHost.NewWebClientTester(Out);
    private IChatsBackend Backend => field ??= Owner.AppServices.GetRequiredService<IChatsBackend>();
    private IMaintenancesBackend Maintenances => field ??= Owner.AppServices.GetRequiredService<IMaintenancesBackend>();
    private DbHub<ChatDbContext> DbHub => field ??= Owner.AppServices.GetRequiredService<DbHub<ChatDbContext>>();

    protected override async Task DisposeAsync()
    {
        await Owner.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ClearHistoryShouldPurgeEntriesUpToTheBoundaryAndPreserveTheRest()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var first = await Owner.CreateTextEntry(chatId, "cleared");
        var last = await Owner.CreateTextEntry(chatId, "kept");
        var range = Constants.Chat.EntryIdTiles.GetTile(first.LocalId).Range;
        var tile = await Computed.Capture(() => Backend.GetTile(chatId, range, true, default));
        var setPinnedCmd = new Chats_SetPinned {
            Session = Owner.Session, EntryId = first.Id, MustPin = true,
        };
        await Owner.Commander.Call(setPinnedCmd);

        // act
        var clearUntilEntryLid = await Clear(chatId, last.LocalId - 1);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, clearUntilEntryLid));

        // assert
        clearUntilEntryLid.Should().Be(last.LocalId - 1);
        await WhenCleanupFlow(chatId, f => f.ClearUntilEntryLid.Should().Be(clearUntilEntryLid));
        tile.IsConsistent().Should().BeFalse("purging an entry invalidates the tile that cached it");
        (await Backend.GetEntry(first.Id, default)).Should().BeNull();
        (await Backend.GetEntry(last.Id, default))!.Content.Should().Be("kept");
        var query = new ChangedEntriesQuery { ChatId = chatId, Limit = 100 };
        var changed = await Backend.ListChangedEntries(query, default);
        changed.Should().OnlyContain(e => e.LocalId > clearUntilEntryLid || e.IsRemoved,
            "an indexer paging by version must never see cleared content as live");
        (await Owner.Chats.ListPinnedEntries(Owner.Session, chatId, default)).Should().BeEmpty();
        (await Owner.Chats.Get(Owner.Session, chatId, default))!.MaintenanceMode.Should().Be(MaintenanceMode.None);
        var newEntry = await Owner.CreateTextEntry(chatId, "after cleanup");
        newEntry.LocalId.Should().BeGreaterThan(last.LocalId);
    }

    [Fact]
    public async Task ClearHistoryShouldBeDrainedByTheCleanupFlow()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var cleared = await Owner.CreateTextEntries(chatId, "cleared", 3);
        var kept = await Owner.CreateTextEntries(chatId, "kept", 2);

        // act
        var clearUntilEntryLid = await Clear(chatId, cleared[^1].LocalId);

        // assert
        clearUntilEntryLid.Should().Be(cleared[^1].LocalId);
        await WhenCleanupFlow(chatId, f => f.ClearUntilEntryLid.Should().Be(clearUntilEntryLid));
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            var rows = db.ChatEntries.Where(e => e.ChatId == chatId.Value && !e.IsRemovedAndPurged);
            (await rows.AnyAsync(e => e.LocalId <= clearUntilEntryLid)).Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        foreach (var entry in cleared)
            (await Backend.GetEntry(entry.Id, default)).Should().BeNull();
        foreach (var entry in kept)
            (await Backend.GetEntry(entry.Id, default))!.Content.Should().Be(entry.Content);
    }

    [Fact]
    public async Task ClearHistoryShouldOnlyMoveForward()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var first = await Owner.CreateTextEntry(chatId, "first");
        var middle = await Owner.CreateTextEntry(chatId, "middle");
        var last = await Owner.CreateTextEntry(chatId, "last");
        await Clear(chatId, middle.LocalId);
        var flow = await WhenCleanupFlow(chatId, f => f.ClearUntilEntryLid.Should().Be(middle.LocalId));

        // act
        var clearUntilEntryLid = await Clear(chatId, first.LocalId);

        // assert
        clearUntilEntryLid.Should().Be(first.LocalId, "the command returns the boundary it was asked for");
        // The lower clear resumes the flow, so a later version with an empty inbox has taken it
        await WhenCleanupFlow(chatId, f => {
            f.Version.Should().BeGreaterThan(flow.Version);
            f.ClearUntilEntryLid.Should().Be(middle.LocalId, "a lower boundary never moves the flow's one back");
        });
        (await Backend.GetEntry(middle.Id, default)).Should().BeNull();
        (await Backend.GetEntry(last.Id, default))!.Content.Should().Be("last");
        (await Clear(chatId, last.LocalId)).Should().Be(last.LocalId);
        await WhenCleanupFlow(chatId, f => f.ClearUntilEntryLid.Should().Be(last.LocalId));
    }

    [Fact]
    public async Task ClearHistoryShouldLeaveLaterMessagesToLaterCleanups()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var cleared = await Owner.CreateTextEntry(chatId, "cleared");
        await Clear(chatId, cleared.LocalId);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, cleared.LocalId));

        // act
        var later = await Owner.CreateTextEntries(chatId, "posted after clearing", 3);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, cleared.LocalId));
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, cleared.LocalId));

        // assert
        (await Backend.GetEntry(cleared.Id, default)).Should().BeNull();
        foreach (var entry in later)
            (await Backend.GetEntry(entry.Id, default))!.Content.Should().Be(entry.Content);
        var lidRange = new Range<long>(cleared.LocalId, later[^1].LocalId + 1);
        var tileEntryLids = new List<long>();
        foreach (var idTile in Constants.Chat.EntryIdTiles.GetCoveringTiles(lidRange)) {
            var tile = await Backend.GetTile(chatId, idTile.Range, false, default);
            tileEntryLids.AddRange(tile.Entries.Select(e => e.LocalId));
        }
        tileEntryLids.Should().Contain(later.Select(e => e.LocalId)).And.NotContain(cleared.LocalId);
        await using var db = await DbHub.CreateDbContext();
        var laterEntryLids = later.Select(e => e.LocalId).ToList();
        var rows = db.ChatEntries.Where(e => e.ChatId == chatId.Value && !e.IsRemovedAndPurged);
        (await rows.AnyAsync(e => e.LocalId <= cleared.LocalId)).Should().BeFalse();
        (await rows.CountAsync(e => laterEntryLids.Contains(e.LocalId))).Should().Be(later.Length);
    }

    [Fact]
    public async Task CleanupShouldNotInvalidateTilesAboveTheBoundary()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entries = new List<ChatEntry>();
        var tileSize = Constants.Chat.EntryIdTiles.TileSize;
        for (var i = 0; i < tileSize * 3; i++)
            entries.Add(await Owner.CreateTextEntry(chatId, $"entry {i}"));
        var cleared = entries[0];
        var kept = entries[^1];
        var clearedRange = Constants.Chat.EntryIdTiles.GetTile(cleared.LocalId).Range;
        var keptRange = Constants.Chat.EntryIdTiles.GetTile(kept.LocalId).Range;
        keptRange.Should().NotBe(clearedRange);
        var clearedTile = await Computed.Capture(() => Backend.GetTile(chatId, clearedRange, true, default));
        var keptTile = await Computed.Capture(() => Backend.GetTile(chatId, keptRange, true, default));

        // act
        await Clear(chatId, cleared.LocalId);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, cleared.LocalId));

        // assert
        clearedTile.IsConsistent().Should().BeFalse();
        keptTile.IsConsistent().Should().BeTrue();
        (await Backend.GetEntry(cleared.Id, default)).Should().BeNull();
        (await Backend.GetEntry(kept.Id, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task CleanupShouldPurgeClearedEntriesAndNeverReuseTheirIds()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var first = await Owner.CreateTextEntry(chatId, "first");
        var last = await Owner.CreateTextEntry(chatId, "last");

        // act
        await Clear(chatId, last.LocalId);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, last.LocalId));
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, last.LocalId));

        // assert
        await using var db = await DbHub.CreateDbContext();
        var rows = await db.ChatEntries.Where(e => e.ChatId == chatId.Value).ToListAsync();
        rows.Should().ContainSingle("only the tombstone at the chat's max LocalId stays");
        rows[0].LocalId.Should().Be(last.LocalId);
        rows[0].Content.Should().BeEmpty();
        rows[0].IsRemovedAndPurged.Should().BeTrue();
        (await Backend.GetEntry(first.Id, default)).Should().BeNull();
        (await Backend.GetEntry(last.Id, default)).Should().BeNull();
        (await Backend.GetLidRange(chatId, false, default)).End.Should()
            .BeLessThanOrEqualTo(first.LocalId, "no cleared id is left in the readable range");
        var query = new ChangedEntriesQuery { ChatId = chatId, Limit = 100 };
        var changed = await Backend.ListChangedEntries(query, default);
        changed.Should().ContainSingle(e => e.LocalId == last.LocalId)
            .Which.IsRemoved.Should().BeTrue("the tombstone is reported as removed, so indexers drop it");
        var next = await Owner.CreateTextEntry(chatId, "new history");
        next.LocalId.Should().BeGreaterThan(last.LocalId);
        var changeEntryCmd = new ChatsBackend_ChangeEntry(
            last.Id, null, Change.Update(new ChatEntryDiff { IsRemoved = false }));
        await FluentActions.Awaiting(() => Owner.Commander.Call(changeEntryCmd))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task WipeHistoryShouldRequireOwnership()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "keep");
        await using var member = AppHost.NewWebClientTester(Out);
        await member.SignInAsUniqueBob();

        // act, assert
        var wipeHistoryCmd = new Chats_WipeHistory { Session = member.Session, ChatId = chatId };
        await FluentActions.Awaiting(() => member.Commander.Call(wipeHistoryCmd))
            .Should().ThrowAsync<Exception>();
        var flowId = FlowHub.NewId<ChatPurgeFlow>(chatId.Value);
        var flow = await FlowHub.TryGet<ChatPurgeFlow>(flowId);
        (flow?.ClearUntilEntryLid ?? 0).Should().Be(0);
        (await FlowHub.GetInboxNonComputed(flowId)).Should().BeEmpty("a rejected clear posts nothing");
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));
        (await Backend.GetEntry(entry.Id, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task WipeShouldStopAtTheLastEntryThereIsWhenRequested()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var wiped = await Owner.CreateTextEntry(chatId, "wiped");

        // act
        var clearUntilEntryLid = await Clear(chatId, wiped.LocalId + 1000);
        var later = await Owner.CreateTextEntry(chatId, "posted after the wipe");

        // assert
        // The member-joined entry can land after the wiped one, so the last entry may be either
        clearUntilEntryLid.Should().BeGreaterThanOrEqualTo(wiped.LocalId)
            .And.BeLessThan(later.LocalId, "the range is clamped to the entries there are");
        await TestWait.WhenPolled(async () => (await Backend.GetEntry(wiped.Id, default)).Should().BeNull(),
            TimeSpan.FromSeconds(30));
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, clearUntilEntryLid));
        (await Backend.GetEntry(later.Id, default))!.Content.Should().Be("posted after the wipe");
    }

    [Fact]
    public async Task PurgeEntryBatchShouldRejectBoundariesPastTheEnd()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "keep");

        // act, assert
        await FluentActions.Awaiting(() => Owner.Commander.Call(
                new ChatsBackend_PurgeEntryBatch(chatId, entry.LocalId + 1000)))
            .Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, -1)))
            .Should().ThrowAsync<Exception>();
        (await Backend.GetEntry(entry.Id, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task PartiallyClearedConversationShouldStayWithItsRemainingMessages()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var first = await Owner.CreateTextEntry(chatId, "old");
        var last = await Owner.CreateTextEntry(chatId, "keep");
        var id = ConversationId.New(chatId, first.LocalId);
        var conversations = Owner.AppServices.GetRequiredService<IConversationsBackend>();
        var changeConversationCmd = new ConversationBackend_Change(id, null, Change.Create(new ConversationDiff {
            Title = "title", Description = "description", Summary = "summary",
            EndEntryLid = last.LocalId, MessageCount = 2,
            StartsAt = first.BeginsAt, EndsAt = last.BeginsAt,
        }));
        await Owner.Commander.Call(changeConversationCmd);
        var tileRange = Constants.Chat.ConversationIdTiles.GetTile(first.LocalId).Range;
        var cached = await Computed.Capture(() => conversations.GetTile(chatId, tileRange, default));

        // act
        await Clear(chatId, last.LocalId - 1);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, last.LocalId - 1));

        // assert
        cached.IsConsistent().Should().BeTrue("the conversation is re-summarized later, not removed");
        (await conversations.Get(id, default)).Should().NotBeNull();
        (await HasScheduledRefresh(id)).Should().BeTrue();
        (await Backend.GetEntry(first.Id, default)).Should().BeNull();
        (await Backend.GetEntry(last.Id, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task PurgedRemovalsShouldResummarizeOrDropTheirConversations()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var alone = await Owner.CreateTextEntry(chatId, "alone");
        var removed = await Owner.CreateTextEntry(chatId, "removed");
        var kept = await Owner.CreateTextEntry(chatId, "kept");
        await Owner.CreateTextEntry(chatId, "a later message, so no removed entry is the last one");
        var emptiedId = await CreateConversation(alone, alone);
        var shrunkId = await CreateConversation(removed, kept);
        await Owner.RemoveTextEntry(alone.Id);
        await Owner.RemoveTextEntry(removed.Id);
        await using var db = await DbHub.CreateDbContext(true);
        await db.ChatEntries.Where(e => e.Id == alone.Id.Value || e.Id == removed.Id.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.RemovedAt, DateTime.UtcNow.AddHours(-2)));
        var conversations = Owner.AppServices.GetRequiredService<IConversationsBackend>();

        // act
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));

        // assert
        (await HasUnpurgedEntry(alone.Id)).Should().BeFalse();
        (await HasUnpurgedEntry(removed.Id)).Should().BeFalse();
        (await conversations.Get(emptiedId, default)).Should().BeNull("none of its messages is left");
        (await conversations.Get(shrunkId, default)).Should().NotBeNull("one of its messages stays");
        (await HasScheduledRefresh(shrunkId)).Should().BeTrue();
        (await HasScheduledRefresh(emptiedId)).Should().BeFalse();
    }

    [Fact]
    public async Task RetentionShouldAdvanceOnlyPastExpiredMessages()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var old = await Owner.CreateTextEntry(chatId, "old");
        var recent = await Owner.CreateTextEntry(chatId, "recent");
        await ExpireEntries(chatId, recent.LocalId - 1);
        await SetRetention(chatId, TimeSpan.FromDays(1));

        // act
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));

        // assert
        (await Backend.GetEntry(old.Id, default)).Should().BeNull();
        (await Backend.GetEntry(recent.Id, default)).Should().NotBeNull();
        var flow = await FlowHub.TryGet<ChatPurgeFlow>(chatId.Value);
        (flow?.ClearUntilEntryLid ?? 0).Should()
            .Be(0, "retention is computed on every cleanup run, nothing is persisted");
        await SetRetention(chatId, null);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));
        (await Backend.GetEntry(old.Id, default)).Should().BeNull();
        (await Backend.GetEntry(recent.Id, default))!.Content.Should().Be("recent");
    }

    [Fact]
    public async Task OrdinaryRemovalShouldKeepItsGracePeriod()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "remove");
        await Owner.RemoveTextEntry(entry.Id);

        // act
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));

        // assert
        await using var db = await DbHub.CreateDbContext(true);
        var row = await db.ChatEntries.SingleAsync(e => e.Id == entry.Id.Value);
        row.IsRemovedAndPurged.Should().BeFalse();
        row.RemovedAt.Should().NotBeNull();
        await db.ChatEntries.Where(e => e.Id == entry.Id.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.RemovedAt, DateTime.UtcNow.AddHours(-2)));
        await db.Entry(row).ReloadAsync();
        row.RemovedAt.Should().BeBefore(DateTime.UtcNow.AddHours(-1));
        var purgedCount = await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));
        purgedCount.Should().BeGreaterThan(0);
        var remainingRow = await db.ChatEntries.AsNoTracking().SingleOrDefaultAsync(e => e.Id == entry.Id.Value);
        (remainingRow is null || remainingRow.IsRemovedAndPurged && remainingRow.Content == "").Should().BeTrue();
    }

    [Fact]
    public async Task RetentionShouldWaitForTheWholeConversation()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var first = await Owner.CreateTextEntry(chatId, "old beginning");
        var last = await Owner.CreateTextEntry(chatId, "recent end");
        var id = ConversationId.New(chatId, first.LocalId);
        var changeConversationCmd = new ConversationBackend_Change(id, null, Change.Create(new ConversationDiff {
            Title = "conversation", Description = "description", Summary = "summary",
            EndEntryLid = last.LocalId, MessageCount = 2,
            StartsAt = first.BeginsAt - TimeSpan.FromDays(2), EndsAt = last.BeginsAt,
        }));
        await Owner.Commander.Call(changeConversationCmd);
        await ExpireEntries(chatId, last.LocalId - 1);
        await SetRetention(chatId, TimeSpan.FromDays(1));

        // act
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));

        // assert
        (await Backend.GetEntry(first.Id, default)).Should()
            .NotBeNull("the conversation it starts hasn't expired yet");
        await ExpireEntries(chatId, last.LocalId);
        await using (var db = await DbHub.CreateDbContext(true))
            await db.Conversations.Where(c => c.Id == id.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.EndsAt, DateTime.UtcNow.AddDays(-2)));
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));
        (await Backend.GetEntry(first.Id, default)).Should().BeNull();
        (await Backend.GetEntry(last.Id, default)).Should().BeNull();
    }

    [Fact]
    public async Task RetentionShouldWaitForTheLiveSession()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var old = await Owner.CreateTextEntry(chatId, "before the session");
        var author = await Owner.GetOwnAuthor(chatId);
        var live = Owner.AppServices.GetRequiredService<ILiveSessionsBackend>();
        // Two streamers latch the session, which is what gives it a visible start
        await live.OnStreamRegistered(chatId, author.Require().Id, null, true, true, default);
        await live.OnStreamRegistered(chatId, AuthorId.New(chatId, 777_072), null, true, true, default);
        var visibleStartLid = await TestWait.When(async ct => {
            var lid = await live.GetVisibleStartLid(chatId, ct);
            lid.Should().NotBeNull();
            return lid!.Value;
        });
        var during = await Owner.CreateTextEntry(chatId, "during the session");
        during.LocalId.Should().BeGreaterThanOrEqualTo(visibleStartLid);
        await ExpireEntries(chatId, during.LocalId);
        await SetRetention(chatId, TimeSpan.FromDays(1));

        // act
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));

        // assert
        (await Backend.GetEntry(old.Id, default)).Should().BeNull();
        (await Backend.GetEntry(during.Id, default)).Should()
            .NotBeNull("the live session still shows it, so retention has to wait for the session");
    }

    [Fact]
    public async Task RemovedThreadAnchorShouldKeepTheThread()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var anchor = await Owner.CreateTextEntry(chatId, "thread anchor");
        var startThreadCmd = new ChatThreads_Start {
            Session = Owner.Session, ParentChatId = chatId,
            Title = "thread", Description = "", EntryIds = [anchor.Id],
        };
        var thread = await Owner.Commander.Call(startThreadCmd);
        var reply = await Owner.CreateTextEntry(thread.Id, "thread reply");
        await Owner.CreateTextEntry(chatId, "a later message, so the anchor isn't the last one");
        await Owner.RemoveTextEntry(anchor.Id);
        await using var db = await DbHub.CreateDbContext(true);
        await db.ChatEntries.Where(e => e.Id == anchor.Id.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.RemovedAt, DateTime.UtcNow.AddHours(-2)));

        // act
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));

        // assert
        var anchorRow = await db.ChatEntries.AsNoTracking().SingleAsync(e => e.Id == anchor.Id.Value);
        anchorRow.IsRemovedAndPurged.Should().BeTrue();
        anchorRow.IsThreadStartEntry.Should().BeTrue();
        anchorRow.Content.Should().BeEmpty();
        (await Backend.IsRemovalPending(thread.Id, default)).Should().BeFalse();
        (await Backend.GetEntry(reply.Id, default))!.Content.Should().Be("thread reply");
    }

    [Fact]
    public async Task ClearedThreadAnchorShouldRemoveTheThread()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var anchor = await Owner.CreateTextEntry(chatId, "thread anchor");
        var startThreadCmd = new ChatThreads_Start {
            Session = Owner.Session, ParentChatId = chatId,
            Title = "thread", Description = "", EntryIds = [anchor.Id],
        };
        var thread = await Owner.Commander.Call(startThreadCmd);
        var child = await Owner.CreateTextEntry(thread.Id, "thread content");
        var kept = await Owner.CreateTextEntry(chatId, "after the anchor");

        // act
        await Clear(chatId, anchor.LocalId);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, anchor.LocalId));

        // assert
        await TestWait.When(async ct => (await Owner.Chats.Get(Owner.Session, thread.Id, ct)).Should().BeNull());
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.Chats.AnyAsync(c => c.Id == thread.Id.Value)).Should().BeFalse();
            (await db.ChatEntries.AnyAsync(e => e.ChatId == thread.Id.Value)).Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        await TestWait.When(async ct => {
            (await Backend.GetEntry(child.Id, ct)).Should().BeNull();
            (await Maintenances.Get(thread.Id.ToMaintenanceKey(), ct)).Should().Be(Maintenance.None);
        });
        (await Backend.GetEntry(kept.Id, default))!.Content.Should().Be("after the anchor");
        (await Backend.Get(chatId, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task CleanupFlowShouldDrainMultipleBatchesAndPreserveAnotherChat()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var (otherId, _) = await Owner.CreateChat(true);
        var other = await Owner.CreateTextEntry(otherId, "untouched");
        ChatEntry? last = null;
        var batchSize = Owner.AppServices.GetRequiredService<ActualChat.Chat.Module.ChatSettings>().CleanupBatchSize;
        for (var i = 0; i <= batchSize; i++)
            last = await Owner.CreateTextEntry(chatId, $"entry {i}");

        // act
        await Clear(chatId, last!.LocalId);

        // assert
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.ChatEntries.CountAsync(e => e.ChatId == chatId.Value && !e.IsRemovedAndPurged)).Should().Be(0);
        }, TimeSpan.FromSeconds(30));
        (await Backend.GetEntry(other.Id, default))!.Content.Should().Be("untouched");
        await using var finalDb = await DbHub.CreateDbContext();
        (await finalDb.ChatEntries.CountAsync(e => e.ChatId == chatId.Value)).Should().Be(1);
    }

    [Fact]
    public async Task ChatRemovalShouldRemoveItsPictureButKeepSystemMedia()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var (systemPictureChatId, _) = await Owner.CreateChat(true);
        var pictureId = MediaId.New(chatId.Value);
        var systemId = MediaId.New(MediaId.SystemIconsScope);
        var media = Owner.AppServices.GetRequiredService<IMediaBackend>();
        foreach (var id in new[] { pictureId, systemId })
            await Owner.Commander.Call(new MediaBackend_Change(id, null, Change.Create(new MediaFull(id))));
        await using (var db = await DbHub.CreateDbContext(true)) {
            await db.Chats.Where(c => c.Id == chatId.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.MediaId, pictureId.Value));
            await db.Chats.Where(c => c.Id == systemPictureChatId.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.MediaId, systemId.Value));
        }

        // act
        await Owner.Commander.Call(new ChatsBackend_RequestRemoval(chatId));
        await Owner.Commander.Call(new ChatsBackend_RequestRemoval(systemPictureChatId));

        // assert
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.Chats.AnyAsync(c => c.Id == chatId.Value || c.Id == systemPictureChatId.Value))
                .Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        await TestWait.When(async ct => (await media.Get(pictureId, ct)).Should().BeNull());
        (await media.Get(systemId, default)).Should().NotBeNull("system media is shared by every chat showing it");
    }

    [Fact]
    public async Task CleanupShouldKeepAttachmentMediaAndRemoveVoiceAndDubAudio()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var (otherId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "attachments");
        var forwarded = await Owner.CreateTextEntry(otherId, "shared attachment");
        var ownedId = MediaId.New(chatId.Value);
        var sharedId = MediaId.New(chatId.Value);
        var voiceId = MediaId.New(chatId.Value);
        var dubId = MediaId.New(chatId.Value);
        var media = Owner.AppServices.GetRequiredService<IMediaBackend>();
        foreach (var id in new[] { ownedId, sharedId, voiceId, dubId }) {
            var createMediaCmd = new MediaBackend_Change(id, null, Change.Create(new MediaFull(id)));
            await Owner.Commander.Call(createMediaCmd);
        }
        var createAttachmentsCmd = new ChatsBackend_CreateAttachments([
            new ChatEntryAttachment { EntryId = entry.Id, Index = 0, MediaId = ownedId },
            new ChatEntryAttachment { EntryId = entry.Id, Index = 1, MediaId = sharedId },
        ]);
        await Owner.Commander.Call(createAttachmentsCmd);
        var createForwardedAttachmentsCmd = new ChatsBackend_CreateAttachments([
            new ChatEntryAttachment { EntryId = forwarded.Id, Index = 0, MediaId = sharedId },
        ]);
        await Owner.Commander.Call(createForwardedAttachmentsCmd);
        await using (var db = await DbHub.CreateDbContext(true)) {
            await db.ChatEntries.Where(e => e.Id == entry.Id.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.AudioId, voiceId.Value));
            db.Translations.Add(new DbTranslation {
                Id = $"{entry.Id.Value}:es", Version = 1, Content = "adjuntos",
                ChatId = chatId.Value, EntryId = entry.Id.Value, DubMediaId = dubId.Value,
            });
            await db.SaveChangesAsync();
        }
        var cached = await Computed.Capture(() => Backend.GetEntryAttachments(entry.Id, default));

        // act
        await Clear(chatId, entry.LocalId);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, entry.LocalId));

        // assert
        cached.IsConsistent().Should().BeFalse();
        (await Backend.GetEntryAttachments(entry.Id, default)).Should().BeEmpty();
        await TestWait.When(async ct => {
            (await media.Get(voiceId, ct)).Should().BeNull();
            (await media.Get(dubId, ct)).Should().BeNull();
        });
        // Attachment media can be shared, so nothing removes it until references are counted
        (await media.Get(ownedId, default)).Should().NotBeNull();
        (await media.Get(sharedId, default)).Should().NotBeNull();
        (await Backend.GetEntryAttachments(forwarded.Id, default)).Should().ContainSingle();
    }

    [Fact]
    public async Task PurgeShouldRecheckEligibilityOfEveryRequestedEntry()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var first = await Owner.CreateTextEntry(chatId, "cleared first");
        var second = await Owner.CreateTextEntry(chatId, "cleared second");
        var visible = await Owner.CreateTextEntry(chatId, "visible");

        // act
        var purgeEntriesCmd = new ChatsBackend_PurgeEntries(
            chatId, [first.LocalId, visible.LocalId], null, visible.LocalId - 1);
        var purgedCount = await Owner.Commander.Call(purgeEntriesCmd);

        // assert
        purgedCount.Should().Be(1, "an entry above MaxClearedEntryLid isn't eligible, whatever the caller asked for");
        (await Backend.GetEntry(first.Id, default)).Should().BeNull();
        (await Backend.GetEntry(second.Id, default))!.Content.Should().Be("cleared second");
        (await Backend.GetEntry(visible.Id, default))!.Content.Should().Be("visible");
        var noBoundaryPurgeCmd = new ChatsBackend_PurgeEntries(chatId, [second.LocalId]);
        (await Owner.Commander.Call(noBoundaryPurgeCmd)).Should().Be(0);
        (await Backend.GetEntry(second.Id, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task MarkedChatShouldBeGoneToClientsButDrainedByTheBackend()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "owned history");
        var startThreadCmd = new ChatThreads_Start {
            Session = Owner.Session, ParentChatId = chatId, Title = "child",
            Description = "", EntryIds = [entry.Id],
        };
        var thread = await Owner.Commander.Call(startThreadCmd);
        var child = await Owner.CreateTextEntry(thread.Id, "child history");
        var author = (await Owner.GetOwnAuthor(chatId)).Require();
        var key = chatId.ToMaintenanceKey();

        // act
        // The chat row lock keeps ChatPurgeFlow from draining the chat until the test has looked at it
        await using (var lockDb = await DbHub.CreateDbContext(true)) {
            await using var tx = await lockDb.Database.BeginTransactionAsync();
            await lockDb.Chats.ForUpdate().SingleAsync(c => c.Id == chatId.Value);
            await Owner.Commander.Call(new ChatsBackend_RequestRemoval(chatId));

            // assert
            (await Owner.Chats.Get(Owner.Session, chatId, default)).Should().BeNull();
            (await Backend.Get(chatId, default)).Should().NotBeNull("the backend keeps the chat until it's drained");
            (await Backend.IsRemovalPending(chatId, default)).Should().BeTrue();
            (await Backend.GetRules(chatId, author.Id, default)).CanRead().Should().BeFalse();
            (await Maintenances.Get(key, default)).Mode.Should().Be(MaintenanceMode.Removal);
            await TestWait.When(async ct =>
                (await Owner.Chats.Get(Owner.Session, thread.Id, ct)).Should().BeNull());
            await FluentActions.Awaiting(() => Owner.CreateTextEntry(chatId, "cannot resurrect"))
                .Should().ThrowAsync<Exception>();
            await tx.RollbackAsync();
        }
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.Chats.AnyAsync(c => c.Id == chatId.Value || c.Id == thread.Id.Value)).Should().BeFalse();
            (await db.ChatEntries.AnyAsync(e => e.ChatId == chatId.Value || e.ChatId == thread.Id.Value))
                .Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        await TestWait.When(async ct => {
            (await Maintenances.Get(key, ct)).Should().Be(Maintenance.None);
            (await Backend.Get(chatId, ct)).Should().BeNull();
            (await Owner.Chats.Get(Owner.Session, chatId, ct)).Should().BeNull();
            (await Backend.GetEntry(child.Id, ct)).Should().BeNull();
        });
        await FluentActions.Awaiting(() => Owner.CreateTextEntry(chatId, "cannot resurrect"))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task RemovalPendingChatShouldRejectWrites()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "before removal");
        var key = chatId.ToMaintenanceKey();
        // Set directly rather than through ChatsBackend_RequestRemoval: nothing resumes ChatPurgeFlow,
        // so the chat stays pending until the test drives the cleanup itself
        var setRemovalCmd = new MaintenancesBackend_Set(key, MaintenanceMode.Removal) {
            OwnerId = ChatsBackend.RemovalMaintenanceOwnerId,
        };
        await Owner.Commander.Call(setRemovalCmd);
        await TestWait.When(async ct => (await Backend.IsRemovalPending(chatId, ct)).Should().BeTrue());

        // act, assert
        await FluentActions.Awaiting(() => Owner.CreateTextEntry(chatId, "rejected"))
            .Should().ThrowAsync<Exception>();
        var editEntryCmd = new ChatsBackend_ChangeEntry(
            entry.Id, null, Change.Update(new ChatEntryDiff { Content = "edited" }));
        await FluentActions.Awaiting(() => Owner.Commander.Call(editEntryCmd))
            .Should().ThrowAsync<NotFoundException>();
        (await Backend.Get(chatId, default)).Should().NotBeNull();
        (await Backend.GetEntry(entry.Id, default))!.Content.Should().Be("before removal");
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));
        await using var db = await DbHub.CreateDbContext();
        (await db.Chats.AnyAsync(c => c.Id == chatId.Value)).Should().BeFalse();
        await TestWait.When(async ct => (await Maintenances.Get(key, ct)).Should().Be(Maintenance.None));
    }

    [Fact]
    public async Task AccountEntryRemovalShouldPreserveOtherAuthorsIncludingInThreads()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Owner.CreateChat(true);
        var anchor = await Owner.CreateTextEntry(chatId, "keep anchor");
        var startThreadCmd = new ChatThreads_Start {
            Session = Owner.Session, ParentChatId = chatId, Title = "shared thread",
            Description = "", EntryIds = [anchor.Id],
        };
        var thread = await Owner.Commander.Call(startThreadCmd);
        var kept = await Owner.CreateTextEntry(thread.Id, "keep thread message");
        await using var member = AppHost.NewWebClientTester(Out);
        var bob = await member.SignInAsUniqueBob();
        await member.JoinChat(chatId, inviteId);
        var removed = await member.CreateTextEntry(chatId, "remove parent message");
        var removedChild = await member.CreateTextEntry(thread.Id, "remove thread message");
        var conversationId = ConversationId.New(chatId, removed.LocalId);
        var conversations = Owner.AppServices.GetRequiredService<IConversationsBackend>();
        var changeConversationCmd = new ConversationBackend_Change(conversationId, null,
            Change.Create(new ConversationDiff {
                Title = "account summary", Description = "account conversation", Summary = "remove parent message",
                EndEntryLid = removed.LocalId, MessageCount = 1,
                StartsAt = removed.BeginsAt, EndsAt = removed.BeginsAt,
            }));
        await Owner.Commander.Call(changeConversationCmd);
        var cachedConversation = await Computed.Capture(() => conversations.Get(conversationId, default));

        // act
        await Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(bob.Id));

        // assert
        await TestWait.When(async ct => {
            (await Backend.GetEntry(removed.Id, ct)).Should().BeNull();
            (await Backend.GetEntry(removedChild.Id, ct)).Should().BeNull();
        }, TimeSpan.FromSeconds(30));
        cachedConversation.IsConsistent().Should().BeFalse();
        (await conversations.Get(conversationId, default)).Should().BeNull();
        (await Backend.GetEntry(anchor.Id, default)).Should().NotBeNull();
        (await Backend.GetEntry(kept.Id, default))!.Content.Should().Be("keep thread message");
        (await Backend.Get(chatId, default)).Should().NotBeNull();
        await Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(bob.Id));
    }

    [Fact]
    public async Task AccountEntryRemovalShouldKeepThreadsTheAccountStarted()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Owner.CreateChat(true);
        await using var member = AppHost.NewWebClientTester(Out);
        var bob = await member.SignInAsUniqueBob();
        await member.JoinChat(chatId, inviteId);
        var anchor = await member.CreateTextEntry(chatId, "bob's thread anchor");
        var startThreadCmd = new ChatThreads_Start {
            Session = member.Session, ParentChatId = chatId, Title = "bob's thread",
            Description = "", EntryIds = [anchor.Id],
        };
        var thread = await member.Commander.Call(startThreadCmd);
        var reply = await Owner.CreateTextEntry(thread.Id, "alice's reply");

        // act
        await Owner.Commander.Call(new ChatsBackend_RequestUserRemoval(bob.Id));

        // assert
        await TestWait.When(async ct => (await Backend.GetEntry(anchor.Id, ct)).Should().BeNull());
        (await Backend.IsRemovalPending(thread.Id, default)).Should().BeFalse();
        (await Backend.GetEntry(reply.Id, default))!.Content.Should().Be("alice's reply");
    }

    [Fact]
    public async Task DelayedMetadataWritesShouldNotRecreatePurgedContent()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "old link");
        await Clear(chatId, entry.LocalId);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, entry.LocalId));

        // act
        var updateLinkIndexCmd = new ChatsBackend_UpdateChatLinkIndex(chatId, [entry.Id], [
            new LinkItem {
                Id = entry.Id.Value + ":0", EntryId = entry.Id, At = entry.BeginsAt,
                Url = "https://example.com",
            },
        ]);
        await Owner.Commander.Call(updateLinkIndexCmd);

        // assert
        await using var db = await DbHub.CreateDbContext();
        (await db.ChatLinkItems.AnyAsync(i => i.EntryId == entry.Id.Value)).Should().BeFalse();
        var createAttachmentsCmd = new ChatsBackend_CreateAttachments([
            new ChatEntryAttachment { EntryId = entry.Id, Index = 0, MediaId = MediaId.New(chatId.Value) },
        ]);
        await FluentActions.Awaiting(() => Owner.Commander.Call(createAttachmentsCmd))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ClearHistoryShouldWaitForAStreamingEntry()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var streaming = await Owner.CreateTextEntry(chatId, "still recording");
        var last = await Owner.CreateTextEntry(chatId, "cleared");
        await SetContentStreamId(streaming.Id, "test-stream");
        await Clear(chatId, last.LocalId);

        // act
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, last.LocalId));

        // assert
        (await HasUnpurgedEntry(streaming.Id)).Should().BeTrue();
        (await HasUnpurgedEntry(last.Id)).Should().BeFalse();
        await SetContentStreamId(streaming.Id, null);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, last.LocalId));
        (await HasUnpurgedEntry(streaming.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task CleanupShouldWaitForAnImportToEnd()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "cleared once the import ends");
        await Owner.CreateTextEntry(chatId, "kept");
        var key = chatId.ToMaintenanceKey();
        var startImportCmd = new MaintenancesBackend_Set(key, MaintenanceMode.Import) { OwnerId = ImportOwnerId };
        await Owner.Commander.Call(startImportCmd);
        await Owner.Commander.Call(new ChatsBackend_RequestHistoryWipe(chatId, (0, entry.LocalId + 1)));

        // act
        var count = await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, entry.LocalId));

        // assert
        count.Should().Be(0);
        (await HasUnpurgedEntry(entry.Id)).Should().BeTrue();
        var endImportCmd = new MaintenancesBackend_Set(key, MaintenanceMode.None) { OwnerId = ImportOwnerId };
        await Owner.Commander.Call(endImportCmd);
        await TestWait.When(async ct => {
            (await Maintenances.Get(key, ct)).Should().Be(Maintenance.None);
        });
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, entry.LocalId));
        (await HasUnpurgedEntry(entry.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task AuthorPurgeShouldWaitForAnImportToEnd()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "purged once the import ends");
        var key = chatId.ToMaintenanceKey();
        var startImportCmd = new MaintenancesBackend_Set(key, MaintenanceMode.Import) { OwnerId = ImportOwnerId };
        await Owner.Commander.Call(startImportCmd);
        var purgeAuthorEntriesCmd = new ChatsBackend_PurgeAuthorEntries(chatId, entry.AuthorId);

        // act
        var count = await Owner.Commander.Call(purgeAuthorEntriesCmd);

        // assert
        count.Should().BeNegative("an import holds the chat's entries");
        (await HasUnpurgedEntry(entry.Id)).Should().BeTrue();
        var endImportCmd = new MaintenancesBackend_Set(key, MaintenanceMode.None) { OwnerId = ImportOwnerId };
        await Owner.Commander.Call(endImportCmd);
        await TestWait.When(async ct => {
            (await Maintenances.Get(key, ct)).Should().Be(Maintenance.None);
        });
        (await Owner.Commander.Call(purgeAuthorEntriesCmd)).Should().BePositive();
        (await HasUnpurgedEntry(entry.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task PurgedConversationShouldNotBeRecreated()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var first = await Owner.CreateTextEntry(chatId, "first");
        var last = await Owner.CreateTextEntry(chatId, "last");
        var clearUntilEntryLid = await Clear(chatId, last.LocalId);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId, clearUntilEntryLid));
        (await HasUnpurgedEntry(first.Id)).Should().BeFalse();

        // act - a summary that finished after the purge took its messages
        var id = ConversationId.New(chatId, first.LocalId);
        var changeConversationCmd = new ConversationBackend_Change(id, null, Change.Create(new ConversationDiff {
            Title = "title", Description = "description", Summary = "summary",
            EndEntryLid = last.LocalId, MessageCount = 2, StartsAt = first.BeginsAt, EndsAt = last.BeginsAt,
        }));
        var conversation = await Owner.Commander.Call(changeConversationCmd);

        // assert
        conversation.Should().BeNull();
        await using var db = await DbHub.CreateDbContext();
        (await db.Conversations.AnyAsync(c => c.Id == id.Value)).Should().BeFalse();
    }

    [Fact]
    public async Task CleanupShouldReclaimATombstoneANewerEntryTookTheMaxLidFrom()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await Owner.AppServices.WaitForOpeningEntry(chatId);
        var removed = await Owner.CreateTextEntry(chatId, "removed");
        await Owner.RemoveTextEntry(removed.Id);
        await using var db = await DbHub.CreateDbContext(true);
        await db.ChatEntries.Where(e => e.Id == removed.Id.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.RemovedAt, DateTime.UtcNow.AddHours(-2)));
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));
        var tombstone = await db.ChatEntries.AsNoTracking().SingleAsync(e => e.Id == removed.Id.Value);
        tombstone.IsRemovedAndPurged.Should().BeTrue("the purged entry holds the chat's max LocalId");
        var next = await Owner.CreateTextEntry(chatId, "next");

        // act
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));

        // assert
        (await db.ChatEntries.AnyAsync(e => e.Id == removed.Id.Value)).Should()
            .BeFalse("a tombstone that no longer holds the max LocalId isn't needed");
        (await Backend.GetEntry(next.Id, default))!.Content.Should().Be("next");
    }

    [Fact]
    public async Task RetentionShouldKeepAThreadAnchorWhileTheThreadHasLiveReplies()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await Owner.AppServices.WaitForOpeningEntry(chatId);
        var old = await Owner.CreateTextEntry(chatId, "old");
        var anchor = await Owner.CreateTextEntry(chatId, "thread anchor");
        var startThreadCmd = new ChatThreads_Start {
            Session = Owner.Session, ParentChatId = chatId,
            Title = "thread", Description = "", EntryIds = [anchor.Id],
        };
        var thread = await Owner.Commander.Call(startThreadCmd);
        var reply = await Owner.CreateTextEntry(thread.Id, "recent reply");
        var kept = await Owner.CreateTextEntry(chatId, "recent");
        await ExpireEntries(chatId, anchor.LocalId);
        await ExpireEntries(thread.Id, reply.LocalId - 1);
        await SetRetention(chatId, TimeSpan.FromDays(1));

        // act
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));

        // assert
        (await Backend.GetEntry(old.Id, default)).Should().BeNull();
        (await Backend.GetEntry(anchor.Id, default)).Should()
            .NotBeNull("the thread it anchors has a reply retention hasn't expired yet");
        (await Backend.IsRemovalPending(thread.Id, default)).Should().BeFalse();
        (await Backend.GetEntry(reply.Id, default))!.Content.Should().Be("recent reply");
        await ExpireEntries(thread.Id, long.MaxValue);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntryBatch(chatId));
        (await Backend.GetEntry(anchor.Id, default)).Should().BeNull();
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.Chats.AnyAsync(c => c.Id == thread.Id.Value)).Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        (await Backend.GetEntry(kept.Id, default))!.Content.Should().Be("recent");
    }

    // Private methods

    private async Task<long> Clear(ChatId chatId, long clearUntilEntryLid)
    {
        var requestHistoryWipeCmd = new ChatsBackend_RequestHistoryWipe(chatId, (0, clearUntilEntryLid + 1));
        var wipedRange = await Owner.Commander.Call(requestHistoryWipeCmd);
        return wipedRange.End - 1;
    }

    private Task<ChatPurgeFlow> WhenCleanupFlow(
        ChatId chatId,
        Action<ChatPurgeFlow> assertion,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLine = 0)
        // Polled: the flow drains its inbox in the background, and the inbox isn't read through a compute method
        => TestWait.WhenPolled<ChatPurgeFlow>(async () => {
            var flowId = FlowHub.NewId<ChatPurgeFlow>(chatId.Value);
            var flow = await FlowHub.TryGet<ChatPurgeFlow>(flowId);
            flow.Should().NotBeNull();
            (await FlowHub.GetInboxNonComputed(flowId)).Should().BeEmpty();
            assertion(flow!);
            return flow!;
        }, TimeSpan.FromSeconds(30), callerFilePath: callerFilePath, callerLine: callerLine);

    private async Task<ConversationId> CreateConversation(ChatEntry first, ChatEntry last)
    {
        var id = ConversationId.New(first.ChatId, first.LocalId);
        var changeConversationCmd = new ConversationBackend_Change(id, null, Change.Create(new ConversationDiff {
            Title = "title", Description = "description", Summary = "summary",
            EndEntryLid = last.LocalId, MessageCount = (int)(last.LocalId - first.LocalId + 1),
            StartsAt = first.BeginsAt, EndsAt = last.BeginsAt,
        }));
        await Owner.Commander.Call(changeConversationCmd);
        return id;
    }

    private async Task<bool> HasScheduledRefresh(ConversationId conversationId)
    {
        await using var db = await DbHub.CreateDbContext();
        var flowId = FlowHub.NewId<ConversationRefreshFlow>(conversationId.Value);
        return await db.Events.AnyAsync(e => e.Uuid.Contains(flowId.Value));
    }

    private async Task SetContentStreamId(ChatEntryId entryId, string? contentStreamId)
    {
        await using var db = await DbHub.CreateDbContext(true);
        await db.ChatEntries.Where(e => e.Id == entryId.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ContentStreamId, contentStreamId));
    }

    private async Task<bool> HasUnpurgedEntry(ChatEntryId entryId)
    {
        await using var db = await DbHub.CreateDbContext();
        return await db.ChatEntries.AnyAsync(e => e.Id == entryId.Value && !e.IsRemovedAndPurged);
    }

    private async Task ExpireEntries(ChatId chatId, long maxLid)
    {
        await using var db = await DbHub.CreateDbContext(true);
        await db.ChatEntries.Where(e => e.ChatId == chatId.Value && e.LocalId <= maxLid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.BeginsAt, DateTime.UtcNow.AddDays(-2))
                .SetProperty(e => e.EndsAt, DateTime.UtcNow.AddDays(-2)));
    }

    private Task SetRetention(ChatId chatId, TimeSpan? retentionPeriod)
    {
        var setRetentionCmd = new Chats_Change {
            Session = Owner.Session, ChatId = chatId, ExpectedVersion = null,
            Change = Change.Update(new ChatDiff { RetentionPeriod = retentionPeriod }),
        };
        return Owner.Commander.Call(setRetentionCmd);
    }
}
