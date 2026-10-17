using ActualChat.Chat.Db;
using ActualChat.Chat.Flows;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public sealed class ForwardedChatFixupFlowTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Owner => field ??= AppHost.NewWebClientTester(Out);
    private DbHub<ChatDbContext> DbHub => field ??= Owner.AppServices.GetRequiredService<DbHub<ChatDbContext>>();

    protected override async Task DisposeAsync()
    {
        await Owner.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task FlowShouldRepointForwardsAtThePublishedCopyOfARemovedChat()
    {
        // arrange
        await Owner.SignInAsUniqueAlice();
        var (copyChatId, _) = await Owner.CreateChat(true);
        var (liveChatId, _) = await Owner.CreateChat(true);
        var (targetChatId, _) = await Owner.CreateChat(true);
        var removedChatId = GroupChatId.New();
        var copiedEntry = await Owner.CreateTextEntry(copyChatId, "copied message");
        var forwardedFromRemoved = await Owner.CreateTextEntry(targetChatId, "forwarded from a removed chat");
        var forwardedFromLive = await Owner.CreateTextEntry(targetChatId, "forwarded from a live chat");
        var notForwarded = await Owner.CreateTextEntry(targetChatId, "not forwarded");
        await SetForwardedFrom(forwardedFromRemoved.Id, ChatEntryId.New(removedChatId, copiedEntry.LocalId));
        await SetForwardedFrom(forwardedFromLive.Id, ChatEntryId.New(liveChatId, 8));
        await AddPublishedCopy(removedChatId, copyChatId);

        // act
        await FlowHub.NewResumeEvent<ForwardedChatFixupFlow>("").WithReset(true).Schedule();

        // assert
        await TestWait.WhenPolled(async () => {
            (await GetForwarded(forwardedFromRemoved.Id)).Should()
                .Be((copiedEntry.Id.Value, copiedEntry.AuthorId.Value));
        }, TimeSpan.FromSeconds(30));
        (await GetForwarded(forwardedFromLive.Id)).Should()
            .Be((ChatEntryId.New(liveChatId, 8).Value, AuthorId.New(liveChatId, 1).Value));
        (await GetForwarded(notForwarded.Id)).Should().Be((null, null));
    }

    [Fact]
    public async Task FlowShouldResumeFromTheCursorWhenAChatHasMoreForwardsThanOneBatch()
    {
        // arrange
        const int forwardCount = 205;
        await Owner.SignInAsUniqueAlice();
        var (copyChatId, _) = await Owner.CreateChat(true);
        var (targetChatId, _) = await Owner.CreateChat(true);
        var removedChatId = GroupChatId.New();
        var copiedEntry = await Owner.CreateTextEntry(copyChatId, "copied message");
        var forwardIds = new List<ChatEntryId>();
        for (var i = 0; i < forwardCount; i++)
            forwardIds.Add((await Owner.CreateTextEntry(targetChatId, $"forward {i}")).Id);
        await using (var db = await DbHub.CreateDbContext(true)) {
            var ids = forwardIds.Select(x => x.Value).ToList();
            await db.ChatEntries.Where(e => ids.Contains(e.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(
                    e => e.ForwardedChatEntryId,
                    ChatEntryId.New(removedChatId, copiedEntry.LocalId).Value));
        }
        await AddPublishedCopy(removedChatId, copyChatId);

        // act
        await FlowHub.NewResumeEvent<ForwardedChatFixupFlow>("").WithReset(true).Schedule();

        // assert
        await TestWait.WhenPolled(async () => {
            await using var db = await DbHub.CreateDbContext();
            var ids = forwardIds.Select(x => x.Value).ToList();
            var fixedCount = await db.ChatEntries
                .CountAsync(e => ids.Contains(e.Id) && e.ForwardedChatEntryId == copiedEntry.Id.Value);
            fixedCount.Should().Be(forwardCount);
        }, TimeSpan.FromSeconds(60));
    }

    // Private methods

    private async Task SetForwardedFrom(ChatEntryId entryId, ChatEntryId sourceEntryId)
    {
        await using var db = await DbHub.CreateDbContext(true);
        await db.ChatEntries.Where(e => e.Id == entryId.Value)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.ForwardedChatEntryId, sourceEntryId.Value)
                .SetProperty(e => e.ForwardedAuthorId, AuthorId.New(sourceEntryId.ChatId, 1).Value));
    }

    private async Task AddPublishedCopy(ChatId sourceChatId, ChatId copyChatId)
    {
        await using var db = await DbHub.CreateDbContext(true);
        db.ChatCopyStates.Add(new DbChatCopyState {
            Id = copyChatId.Value,
            Version = 1,
            SourceChatId = sourceChatId.Value,
            IsPublished = true,
            PublishedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<(string? ChatEntryId, string? AuthorId)> GetForwarded(ChatEntryId entryId)
    {
        await using var db = await DbHub.CreateDbContext();
        var row = await db.ChatEntries
            .Where(e => e.Id == entryId.Value)
            .Select(e => new { e.ForwardedChatEntryId, e.ForwardedAuthorId })
            .SingleAsync();
        return (row.ForwardedChatEntryId, row.ForwardedAuthorId);
    }
}
