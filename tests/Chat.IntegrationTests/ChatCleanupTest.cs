using ActualChat.Chat.Db;
using ActualChat.Media;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class ChatCleanupTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Owner => field ??= AppHost.NewWebClientTester(Out);
    private IChatsBackend Backend => field ??= Owner.AppServices.GetRequiredService<IChatsBackend>();
    private DbHub<ChatDbContext> DbHub => field ??= Owner.AppServices.GetRequiredService<DbHub<ChatDbContext>>();

    protected override async Task DisposeAsync()
    {
        await Owner.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task BoundaryShouldInvalidateCachedHistoryAndPreserveNewMessages()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var first = await Owner.CreateTextEntry(chatId, "hidden");
        var last = await Owner.CreateTextEntry(chatId, "visible");
        var range = Constants.Chat.EntryIdTiles.GetTile(first.LocalId).Range;
        var tile = await Computed.Capture(() => Backend.GetTile(chatId, range, true, default));
        await Owner.Commander.Call(new Chats_SetPinned {
            Session = Owner.Session, EntryId = first.Id, MustPin = true,
        });

        // act
        await Advance(chatId, last.LocalId);

        // assert
        tile.IsConsistent().Should().BeFalse();
        (await Backend.GetEntry(first.Id, default)).Should().BeNull();
        var changed = await Backend.ListChangedEntries(new ChangedEntriesQuery { ChatId = chatId, Limit = 100 }, default);
        changed.Should().OnlyContain(e => e.LocalId >= last.LocalId);
        (await Backend.GetEntry(last.Id, default)).Should().NotBeNull();
        (await Owner.Chats.ListPinnedEntries(Owner.Session, chatId, default)).Should().BeEmpty();
        (await Backend.GetVisibilityBoundary(chatId, default)).Should().Be(last.LocalId);
        var newEntry = await Owner.CreateTextEntry(chatId, "after cleanup");
        newEntry.LocalId.Should().BeGreaterThan(last.LocalId);
        (await Owner.Chats.Get(Owner.Session, chatId, default))!.MaintenanceMode.Should().Be(MaintenanceMode.None);
        await Advance(chatId, first.LocalId);
        (await Backend.GetVisibilityBoundary(chatId, default)).Should().Be(last.LocalId);
    }

    [Fact]
    public async Task CleanupShouldPurgeHiddenEntriesAndNeverReuseTheirIds()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var first = await Owner.CreateTextEntry(chatId, "first");
        var last = await Owner.CreateTextEntry(chatId, "last");

        // act
        await Advance(chatId, last.LocalId + 1);
        await Owner.Commander.Call(new ChatsBackend_Cleanup(chatId));
        await Owner.Commander.Call(new ChatsBackend_Cleanup(chatId));

        // assert
        await using var db = await DbHub.CreateDbContext();
        var rows = await db.ChatEntries.Where(e => e.ChatId == chatId.Value).ToListAsync();
        rows.Should().ContainSingle();
        rows[0].LocalId.Should().Be(last.LocalId);
        rows[0].Content.Should().BeEmpty();
        rows[0].IsPurged.Should().BeTrue();
        (await Backend.GetEntry(first.Id, default)).Should().BeNull();
        (await Backend.GetLidRange(chatId, false, default)).IsEmpty.Should().BeTrue();
        var next = await Owner.CreateTextEntry(chatId, "new history");
        next.LocalId.Should().BeGreaterThan(last.LocalId);
        await FluentActions.Awaiting(() => Owner.Commander.Call(
            new ChatsBackend_ChangeEntry(last.Id, null, Change.Update(new ChatEntryDiff { IsRemoved = false }))))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task BoundaryShouldRequireOwnershipAndRejectFutureIds()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "keep");
        await using var member = AppHost.NewWebClientTester(Out);
        await member.SignInAsUniqueBob();

        // act, assert
        await FluentActions.Awaiting(() => member.Commander.Call(new Chats_Cleanup {
            Session = member.Session, ChatId = chatId, MinVisibleEntryLid = entry.LocalId + 1,
        })).Should().ThrowAsync<Exception>();
        await FluentActions.Awaiting(() => Advance(chatId, entry.LocalId + 1000))
            .Should().ThrowAsync<Exception>();
        (await Backend.GetEntry(entry.Id, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task PartialConversationShouldDisappearWithoutHidingItsRemainingMessages()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var first = await Owner.CreateTextEntry(chatId, "old");
        var last = await Owner.CreateTextEntry(chatId, "keep");
        var id = ConversationId.New(chatId, first.LocalId);
        var conversations = Owner.AppServices.GetRequiredService<IConversationsBackend>();
        await Owner.Commander.Call(new ConversationBackend_Change(id, null, Change.Create(new ConversationDiff {
            Title = "title", Description = "description", Summary = "summary",
            EndEntryLid = last.LocalId, MessageCount = 2,
            StartsAt = first.BeginsAt, EndsAt = last.BeginsAt,
        })));
        var tileRange = Constants.Chat.ConversationIdTiles.GetTile(first.LocalId).Range;
        var cached = await Computed.Capture(() => conversations.GetTile(chatId, tileRange, default));

        // act
        await Advance(chatId, last.LocalId);

        // assert
        cached.IsConsistent().Should().BeFalse();
        (await conversations.Get(id, default)).Should().BeNull();
        (await conversations.GetTile(chatId, tileRange, default)).Should().BeEmpty();
        (await Backend.GetEntry(last.Id, default)).Should().NotBeNull();
    }

    [Fact]
    public async Task RetentionShouldAdvanceOnlyPastExpiredMessages()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var old = await Owner.CreateTextEntry(chatId, "old");
        var recent = await Owner.CreateTextEntry(chatId, "recent");
        await using (var db = await DbHub.CreateDbContext(true)) {
            await db.ChatEntries.Where(e => e.ChatId == chatId.Value && e.LocalId < recent.LocalId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.BeginsAt, DateTime.UtcNow.AddDays(-2))
                    .SetProperty(e => e.EndsAt, DateTime.UtcNow.AddDays(-2)));
        }
        await Owner.Commander.Call(new Chats_Change {
            Session = Owner.Session, ChatId = chatId, ExpectedVersion = null,
            Change = Change.Update(new ChatDiff { RetentionPeriod = TimeSpan.FromDays(1) }),
        });

        // act
        await Owner.Commander.Call(new ChatsBackend_Cleanup(chatId));

        // assert
        (await Backend.GetVisibilityBoundary(chatId, default)).Should().Be(recent.LocalId);
        (await Backend.GetEntry(old.Id, default)).Should().BeNull();
        (await Backend.GetEntry(recent.Id, default)).Should().NotBeNull();
        await Owner.Commander.Call(new Chats_Change {
            Session = Owner.Session, ChatId = chatId, ExpectedVersion = null,
            Change = Change.Update(new ChatDiff { RetentionPeriod = (TimeSpan?)null }),
        });
        (await Backend.GetVisibilityBoundary(chatId, default)).Should().Be(recent.LocalId);
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
        await Owner.Commander.Call(new ChatsBackend_Cleanup(chatId));

        // assert
        await using var db = await DbHub.CreateDbContext(true);
        var row = await db.ChatEntries.SingleAsync(e => e.Id == entry.Id.Value);
        row.IsPurged.Should().BeFalse();
        row.RemovedAt.Should().NotBeNull();
        await db.ChatEntries.Where(e => e.Id == entry.Id.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.RemovedAt, DateTime.UtcNow.AddHours(-2)));
        await db.Entry(row).ReloadAsync();
        row.RemovedAt.Should().BeBefore(DateTime.UtcNow.AddHours(-1));
        var purgedCount = await Owner.Commander.Call(new ChatsBackend_Cleanup(chatId));
        purgedCount.Should().BeGreaterThan(0);
        var remainingRow = await db.ChatEntries.AsNoTracking().SingleOrDefaultAsync(e => e.Id == entry.Id.Value);
        (remainingRow is null || remainingRow.IsPurged && remainingRow.Content == "").Should().BeTrue();
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
        await Owner.Commander.Call(new ConversationBackend_Change(id, null, Change.Create(new ConversationDiff {
            Title = "conversation", Description = "description", Summary = "summary",
            EndEntryLid = last.LocalId, MessageCount = 2,
            StartsAt = first.BeginsAt - TimeSpan.FromDays(2), EndsAt = last.BeginsAt,
        })));
        await using var db = await DbHub.CreateDbContext(true);
        await db.ChatEntries.Where(e => e.ChatId == chatId.Value && e.LocalId < last.LocalId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.BeginsAt, DateTime.UtcNow.AddDays(-2))
                .SetProperty(e => e.EndsAt, DateTime.UtcNow.AddDays(-2)));
        await Owner.Commander.Call(new Chats_Change {
            Session = Owner.Session, ChatId = chatId, ExpectedVersion = null,
            Change = Change.Update(new ChatDiff { RetentionPeriod = TimeSpan.FromDays(1) }),
        });

        // act
        await Owner.Commander.Call(new ChatsBackend_Cleanup(chatId));

        // assert
        (await Backend.GetVisibilityBoundary(chatId, default)).Should().Be(first.LocalId);
        (await Backend.GetEntry(first.Id, default)).Should().NotBeNull();
        await db.ChatEntries.Where(e => e.Id == last.Id.Value)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.BeginsAt, DateTime.UtcNow.AddDays(-2))
                .SetProperty(e => e.EndsAt, DateTime.UtcNow.AddDays(-2)));
        await db.Conversations.Where(c => c.Id == id.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.EndsAt, DateTime.UtcNow.AddDays(-2)));
        await Owner.Commander.Call(new ChatsBackend_Cleanup(chatId));
        (await Backend.GetVisibilityBoundary(chatId, default)).Should().Be(last.LocalId + 1);
        (await Backend.GetEntry(first.Id, default)).Should().BeNull();
    }

    [Fact]
    public async Task HiddenThreadShouldBecomeUnreadableAndBePurged()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var anchor = await Owner.CreateTextEntry(chatId, "thread anchor");
        var thread = await Owner.Commander.Call(new ChatThreads_Start {
            Session = Owner.Session, ParentChatId = chatId,
            Title = "thread", Description = "", EntryIds = [anchor.Id],
        });
        var child = await Owner.CreateTextEntry(thread.Id, "thread content");
        var childTile = Constants.Chat.EntryIdTiles.GetTile(child.LocalId).Range;
        var cached = await Computed.Capture(() => Backend.GetTile(thread.Id, childTile, true, default));

        // act
        await Advance(chatId, anchor.LocalId + 1);

        // assert
        cached.IsConsistent().Should().BeFalse();
        (await Backend.GetEntry(child.Id, default)).Should().BeNull();
        (await Backend.GetVisibilityBoundary(thread.Id, default)).Should().Be(long.MaxValue);
        await Owner.Commander.Call(new ChatsBackend_Cleanup(thread.Id));
        await using var db = await DbHub.CreateDbContext();
        (await db.ChatEntries.Where(e => e.ChatId == thread.Id.Value && !e.IsPurged).CountAsync())
            .Should().Be(0);
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
        await Advance(chatId, last!.LocalId + 1);

        // assert
        await TestExt.When(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.ChatEntries.CountAsync(e => e.ChatId == chatId.Value && !e.IsPurged)).Should().Be(0);
        }, TimeSpan.FromSeconds(30));
        (await Backend.GetEntry(other.Id, default))!.Content.Should().Be("untouched");
        await using var finalDb = await DbHub.CreateDbContext();
        (await finalDb.ChatEntries.CountAsync(e => e.ChatId == chatId.Value)).Should().Be(1);
    }

    [Fact]
    public async Task CleanupShouldRemoveUnreferencedMediaAndPreserveForwardedAttachments()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var (otherId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "attachments");
        var forwarded = await Owner.CreateTextEntry(otherId, "shared attachment");
        var ownedId = MediaId.New(chatId.Value);
        var sharedId = MediaId.New(chatId.Value);
        var media = Owner.AppServices.GetRequiredService<IMediaBackend>();
        foreach (var id in new[] { ownedId, sharedId })
            await Owner.Commander.Call(new MediaBackend_Change(id, null, Change.Create(new MediaFull(id))));
        await Owner.Commander.Call(new ChatsBackend_CreateAttachments([
            new ChatEntryAttachment { EntryId = entry.Id, Index = 0, MediaId = ownedId },
            new ChatEntryAttachment { EntryId = entry.Id, Index = 1, MediaId = sharedId },
        ]));
        await Owner.Commander.Call(new ChatsBackend_CreateAttachments([
            new ChatEntryAttachment { EntryId = forwarded.Id, Index = 0, MediaId = sharedId },
        ]));
        var cached = await Computed.Capture(() => Backend.GetEntryAttachments(entry.Id, default));

        // act
        await Advance(chatId, entry.LocalId + 1);
        await Owner.Commander.Call(new ChatsBackend_Cleanup(chatId));

        // assert
        cached.IsConsistent().Should().BeFalse();
        (await Backend.GetEntryAttachments(entry.Id, default)).Should().BeEmpty();
        await TestExt.When(async () => {
            (await media.Get(ownedId, default)).Should().BeNull();
        }, TimeSpan.FromSeconds(10));
        (await media.Get(sharedId, default)).Should().NotBeNull();
        (await Backend.GetEntryAttachments(forwarded.Id, default)).Should().ContainSingle();
    }

    [Fact]
    public async Task GarbageEnumerationShouldReturnOnlyIdsAndRecheckEligibility()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var first = await Owner.CreateTextEntry(chatId, "hidden first");
        var second = await Owner.CreateTextEntry(chatId, "hidden second");
        var visible = await Owner.CreateTextEntry(chatId, "visible");
        await using var db = await DbHub.CreateDbContext(true);
        await db.Chats.Where(c => c.Id == chatId.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.MinVisibleEntryLid, visible.LocalId));
        using (Invalidation.Begin())
            _ = Backend.GetVisibilityBoundary(chatId, default);

        // act
        var ids = await Backend.ListEntryIdsForCleanup(chatId, new(first.LocalId, visible.LocalId + 1), 2, default);

        // assert
        ids.Should().HaveCount(2).And.BeInAscendingOrder();
        ids[0].Should().Be(first.LocalId);
        ids.Should().OnlyContain(id => id >= first.LocalId && id < visible.LocalId);
        (await Backend.ListEntryIdsForCleanup(chatId, (second.LocalId, second.LocalId + 1), 2, default))
            .Should().Equal(second.LocalId);
        (await Backend.ListChangedEntries(new ChangedEntriesQuery { ChatId = chatId, Limit = 100 }, default))
            .Should().OnlyContain(e => e.LocalId >= visible.LocalId);
        await Owner.Commander.Call(new ChatsBackend_PurgeEntries(chatId, [first.LocalId, visible.LocalId]));
        (await Backend.GetEntry(first.Id, default)).Should().BeNull();
        (await Backend.GetEntry(visible.Id, default)).Should().NotBeNull();
        await FluentActions.Awaiting(() => Owner.Commander.Call(
            new ChatsBackend_ChangeEntry(second.Id, null, Change.Update(new ChatEntryDiff { IsRemoved = false }))))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task AccountChatRemovalShouldHideImmediatelyAndDrainInTheBackground()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "owned history");
        var thread = await Owner.Commander.Call(new ChatThreads_Start {
            Session = Owner.Session, ParentChatId = chatId, Title = "child",
            Description = "", EntryIds = [entry.Id],
        });
        var child = await Owner.CreateTextEntry(thread.Id, "child history");
        var cached = await Computed.Capture(() => Backend.GetTile(
            chatId, Constants.Chat.EntryIdTiles.GetTile(entry.LocalId).Range, true, default));

        // act
        await Owner.Commander.Call(new ChatsBackend_MarkForRemoval(chatId));

        // assert
        cached.IsConsistent().Should().BeFalse();
        (await Backend.Get(chatId, default)).Should().BeNull();
        (await Backend.GetEntry(entry.Id, default)).Should().BeNull();
        (await Backend.GetEntry(child.Id, default)).Should().BeNull();
        await TestExt.When(async () => {
            await using var db = await DbHub.CreateDbContext();
            (await db.Chats.AnyAsync(c => c.Id == chatId.Value || c.Id == thread.Id.Value)).Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        (await Backend.GetEntry(child.Id, default)).Should().BeNull();
        await FluentActions.Awaiting(() => Owner.CreateTextEntry(chatId, "cannot resurrect"))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task AccountEntryRemovalShouldPreserveOtherAuthorsIncludingInThreads()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, inviteId) = await Owner.CreateChat(true);
        var anchor = await Owner.CreateTextEntry(chatId, "keep anchor");
        var thread = await Owner.Commander.Call(new ChatThreads_Start {
            Session = Owner.Session, ParentChatId = chatId, Title = "shared thread",
            Description = "", EntryIds = [anchor.Id],
        });
        var kept = await Owner.CreateTextEntry(thread.Id, "keep thread message");
        await using var member = AppHost.NewWebClientTester(Out);
        var bob = await member.SignInAsUniqueBob();
        await member.JoinChat(chatId, inviteId);
        var removed = await member.CreateTextEntry(chatId, "remove parent message");
        var removedChild = await member.CreateTextEntry(thread.Id, "remove thread message");
        var conversationId = ConversationId.New(chatId, removed.LocalId);
        var conversations = Owner.AppServices.GetRequiredService<IConversationsBackend>();
        await Owner.Commander.Call(new ConversationBackend_Change(conversationId, null,
            Change.Create(new ConversationDiff {
                Title = "account summary", Description = "account conversation", Summary = "remove parent message",
                EndEntryLid = removed.LocalId, MessageCount = 1,
                StartsAt = removed.BeginsAt, EndsAt = removed.BeginsAt,
            })));
        var cachedConversation = await Computed.Capture(() => conversations.Get(conversationId, default));

        // act
        await Owner.Commander.Call(new ChatsBackend_RemoveOwnEntries(bob.Id));

        // assert
        (await Backend.GetEntry(removed.Id, default)).Should().BeNull();
        (await Backend.GetEntry(removedChild.Id, default)).Should().BeNull();
        cachedConversation.IsConsistent().Should().BeFalse();
        (await conversations.Get(conversationId, default)).Should().BeNull();
        (await Backend.GetEntry(anchor.Id, default)).Should().NotBeNull();
        (await Backend.GetEntry(kept.Id, default))!.Content.Should().Be("keep thread message");
        (await Backend.Get(chatId, default)).Should().NotBeNull();
        await Owner.Commander.Call(new ChatsBackend_RemoveOwnEntries(bob.Id));
    }

    [Fact]
    public async Task DelayedMetadataWritesShouldNotRecreatePurgedContent()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entry = await Owner.CreateTextEntry(chatId, "old link");
        await Advance(chatId, entry.LocalId + 1);
        await Owner.Commander.Call(new ChatsBackend_Cleanup(chatId));

        // act
        await Owner.Commander.Call(new ChatsBackend_UpdateChatLinkIndex(chatId, [entry.Id], [
            new LinkItem {
                Id = entry.Id.Value + ":0", EntryId = entry.Id, At = entry.BeginsAt,
                Url = "https://example.com",
            },
        ]));

        // assert
        await using var db = await DbHub.CreateDbContext();
        (await db.ChatLinkItems.AnyAsync(i => i.EntryId == entry.Id.Value)).Should().BeFalse();
        await FluentActions.Awaiting(() => Owner.Commander.Call(new ChatsBackend_CreateAttachments([
            new ChatEntryAttachment { EntryId = entry.Id, Index = 0, MediaId = MediaId.New(chatId.Value) },
        ]))).Should().ThrowAsync<Exception>();
    }

    // Private methods

    private Task<long> Advance(ChatId chatId, long boundary)
        => Owner.Commander.Call(new Chats_Cleanup {
            Session = Owner.Session, ChatId = chatId, MinVisibleEntryLid = boundary,
        });
}
