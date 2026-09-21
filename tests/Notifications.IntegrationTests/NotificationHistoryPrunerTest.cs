using ActualChat.Notifications.Db;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Notifications.IntegrationTests;

[Collection(nameof(NotificationCollection))]
public sealed class NotificationHistoryPrunerTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private NotificationHistoryPruner Pruner
        => field ??= AppHost.Services.GetRequiredService<NotificationHistoryPruner>();

    [Fact]
    public async Task RunOnceShouldPruneOnlyRowsPastRetention()
    {
        // arrange
        var userId = UserId.New();
        var chatId = ChatId.Parse("the-actual-one");
        var dbHub = AppHost.Services.DbHub<NotificationDbContext>();
        var now = Clocks.SystemClock.Now;
        await Insert(dbHub, userId, chatId, 1, now - TimeSpan.FromDays(31));
        var freshId = await Insert(dbHub, userId, chatId, 2, now - TimeSpan.FromMinutes(1));

        // act
        await Pruner.RunOnce(CancellationToken.None);

        // assert
        await using var dbContext = await dbHub.CreateDbContext();
        var remainingIds = await dbContext.NotificationHistory
            .Where(x => x.UserId == userId.Value)
            .Select(x => x.Id)
            .ToListAsync();
        remainingIds.Should().Equal([freshId], "only rows older than HistoryRetention are pruned");
    }

    // Private methods

    private static async Task<string> Insert(
        DbHub<NotificationDbContext> dbHub, UserId userId, ChatId chatId, long lid, Moment createdAt)
    {
        var notification = MentionNotification.New(userId, ChatEntryId.New(chatId, lid), AuthorId.New(chatId, 1))
            with { SentAt = createdAt, Title = "Chat", Text = "@you" };
        var item = new DbNotificationHistoryItem(notification, dbHub.VersionGenerator.NextVersion(), createdAt);
        await using var dbContext = await dbHub.CreateDbContext(readWrite: true);
        dbContext.Add(item);
        await dbContext.SaveChangesAsync();
        return item.Id;
    }
}
