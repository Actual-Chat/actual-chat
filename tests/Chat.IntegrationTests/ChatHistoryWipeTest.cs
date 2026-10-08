using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Flows;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class ChatHistoryWipeTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
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
    public async Task RecentWipeShouldKeepOlderAndLaterEntries()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var older = await Owner.CreateTextEntries(chatId, "older", 2);
        var wiped = await Owner.CreateTextEntries(chatId, "wiped", 3);

        // act
        var wipedRange = await Owner.Commander.Call(new Chats_WipeHistory {
            Session = Owner.Session, ChatId = chatId, MinEntryLid = wiped[0].LocalId,
        });
        var later = await Owner.CreateTextEntry(chatId, "later");

        // assert
        wipedRange.Should().Be(new Range<long>(wiped[0].LocalId, wiped[^1].LocalId + 1));
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            var rows = db.ChatEntries.Where(e => e.ChatId == chatId.Value && !e.IsRemovedAndPurged);
            (await rows.AnyAsync(e => e.LocalId >= wipedRange.Start && e.LocalId < wipedRange.End))
                .Should().BeFalse();
        }, TimeSpan.FromSeconds(30));
        foreach (var entry in older)
            (await Backend.GetEntry(entry.Id, default))!.Content.Should().Be(entry.Content);
        (await Backend.GetEntry(later.Id, default))!.Content.Should().Be("later");
        var systemEntry = await WhenHistoryChangedEntry(chatId, wipedRange.End);
        systemEntry.HistoryChange.Should().Be(HistoryChangeKind.Wiped);
        systemEntry.HistoryPeriod.Should().Be(TimeSpan.FromMinutes(1), "the wiped entries are seconds old");
        systemEntry.TargetAuthorName.Should().NotBeEmpty();
        await WhenPurgeFlow(chatId, f => {
            f.WipeEntryLidRanges.Should().BeEmpty("a purged range is dropped");
            f.ClearUntilEntryLid.Should().Be(0, "a recent wipe doesn't move the boundary");
        });
    }

    [Fact]
    public async Task WipeLeavingNoMessageBeforeItShouldWipeEverything()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        var entries = await Owner.CreateTextEntries(chatId, "wiped", 3);

        // act
        var wipedRange = await Owner.Commander.Call(new Chats_WipeHistory {
            Session = Owner.Session, ChatId = chatId, MinEntryLid = entries[0].LocalId,
        });

        // assert
        wipedRange.Start.Should().Be(0, "no message is left before the first wiped one");
        await WhenPurgeFlow(chatId, f => {
            f.ClearUntilEntryLid.Should().Be(wipedRange.End - 1);
            f.WipeEntryLidRanges.Should().BeEmpty();
        });
        foreach (var entry in entries)
            await TestWait.WhenPolled(async () => (await Backend.GetEntry(entry.Id, default)).Should().BeNull(),
                TimeSpan.FromSeconds(30));
        var systemEntry = await WhenHistoryChangedEntry(chatId, wipedRange.End);
        systemEntry.HistoryChange.Should().Be(HistoryChangeKind.Wiped);
        systemEntry.HistoryPeriod.Should().BeNull("the whole history went");
    }

    [Fact]
    public async Task EitherPeerShouldManageHistory()
    {
        // arrange
        await using var aliceTester = AppHost.NewBlazorTester(Out);
        var alice = await aliceTester.SignInAsUniqueAlice();
        await using var bobTester = AppHost.NewBlazorTester(Out);
        var bob = await bobTester.SignInAsUniqueBob();
        await bobTester.CreatePeerContact(bob, alice);
        var chatId = (ChatId)PeerChatId.New(alice.Id, bob.Id);
        var entry = await aliceTester.Commander.Call(new Chats_UpsertEntry {
            Session = aliceTester.Session, ChatId = chatId, LocalId = null, Text = "Hello!",
        });

        // act
        var chat = await bobTester.Commander.Call(new Chats_Change {
            Session = bobTester.Session,
            ChatId = chatId,
            ExpectedVersion = null,
            Change = Change.Update(new ChatDiff { RetentionPeriod = TimeSpan.FromDays(7) }),
        });
        var retentionEntry = await WhenHistoryChangedEntry(chatId, entry.LocalId + 1);
        var wipedRange = await aliceTester.Commander.Call(new Chats_WipeHistory {
            Session = aliceTester.Session, ChatId = chatId,
        });

        // assert
        chat.RetentionPeriod.Should().Be(TimeSpan.FromDays(7));
        retentionEntry.HistoryChange.Should().Be(HistoryChangeKind.RetentionChanged);
        retentionEntry.HistoryPeriod.Should().Be(TimeSpan.FromDays(7));
        wipedRange.IsEmpty.Should().BeFalse();
        await TestWait.WhenPolled(async () => (await Backend.GetEntry(entry.Id, default)).Should().BeNull(),
            TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GroupMemberShouldNotManageHistory()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (chatId, _) = await Owner.CreateChat(true);
        await Owner.CreateTextEntry(chatId, "kept");
        await using var member = AppHost.NewWebClientTester(Out);
        await member.SignInAsUniqueBob();
        await member.Commander.Call(new Authors_Join { Session = member.Session, ChatId = chatId });

        // act
        var wipe = () => member.Commander.Call(new Chats_WipeHistory { Session = member.Session, ChatId = chatId });
        var changeRetention = () => member.Commander.Call(new Chats_Change {
            Session = member.Session,
            ChatId = chatId,
            ExpectedVersion = null,
            Change = Change.Update(new ChatDiff { RetentionPeriod = TimeSpan.FromDays(1) }),
        });

        // assert
        await wipe.Should().ThrowAsync<Exception>();
        await changeRetention.Should().ThrowAsync<Exception>();
    }

    // Private methods

    private Task<HistoryChangedEntry> WhenHistoryChangedEntry(ChatId chatId, long minLid)
        => TestWait.WhenPolled<HistoryChangedEntry>(async () => {
            var maxLid = await Backend.GetMaxLid(chatId, false, default);
            HistoryChangedEntry? found = null;
            for (var lid = minLid; lid <= maxLid && found is null; lid++)
                found = await Backend.GetEntry(ChatEntryId.New(chatId, lid), default) as HistoryChangedEntry;
            found.Should().NotBeNull();
            return found!;
        }, TimeSpan.FromSeconds(30));

    private Task WhenPurgeFlow(ChatId chatId, Action<ChatPurgeFlow> assertion)
        => TestWait.WhenPolled(async () => {
            var flowId = FlowHub.NewId<ChatPurgeFlow>(chatId.Value);
            var flow = await FlowHub.TryGet<ChatPurgeFlow>(flowId);
            flow.Should().NotBeNull();
            (await FlowHub.GetInboxNonComputed(flowId)).Should().BeEmpty();
            assertion(flow!);
        }, TimeSpan.FromSeconds(30));
}
