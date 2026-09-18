using ActualChat.Chat.Db;
using ActualChat.Testing.Host;
using ActualChat.WebHooks;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Generators;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class WebHookDeliveryPrunerTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Alice => field ??= fixture.AppHost.NewWebClientTester(Out);
    private WebHookDeliveryPruner Pruner => field ??= AppHost.Services.GetRequiredService<WebHookDeliveryPruner>();

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
    public async Task RunOnceShouldPruneOnlyOldTerminalDeliveries()
    {
        // arrange
        var (chatId, _) = await Alice.CreateChat(x => x with { Title = "Delivery pruner" });
        var hookId = await CreateHook(chatId);
        var dbHub = AppHost.Services.DbHub<ChatDbContext>();
        var now = Clocks.SystemClock.Now;
        var oldSucceededId = await InsertDelivery(
            dbHub, hookId, WebHookDeliveryStatus.Succeeded, now - TimeSpan.FromDays(31));
        var freshSucceededId = await InsertDelivery(
            dbHub, hookId, WebHookDeliveryStatus.Succeeded, now - TimeSpan.FromMinutes(1));
        var oldPendingId = await InsertDelivery(
            dbHub, hookId, WebHookDeliveryStatus.Pending, now - TimeSpan.FromDays(31));

        // act
        await Pruner.RunOnce(CancellationToken.None);

        // assert
        await using var dbContext = await dbHub.CreateDbContext();
        var remainingIds = await dbContext.WebHookDeliveries
            .Where(x => x.WebHookId == hookId.Value)
            .Select(x => x.Id)
            .ToListAsync();
        remainingIds.Should().BeEquivalentTo(
            [freshSucceededId, oldPendingId],
            "the pruner deletes terminal deliveries past the retention window, but never a pending one");
        remainingIds.Should().NotContain(oldSucceededId);
    }

    // Private methods

    private async Task<WebHookId> CreateHook(ChatId chatId)
    {
        var alice = await Alice.GetOwnAccount();
        var diff = new WebHookDiff { Name = "CI", Url = "https://example.com/hook", Events = WebHookEvents.Messages };
        var result = await Commander.Call(new WebHooksBackend_Change(
            WebHookScope.Chat, chatId.Value, null, null, Change.Create(diff), alice.Id));
        return result.WebHook!.Id;
    }

    private static async Task<string> InsertDelivery(
        DbHub<ChatDbContext> dbHub, WebHookId hookId, WebHookDeliveryStatus status, Moment createdAt)
    {
        var id = $"{hookId}:{RandomStringGenerator.Default.Next()}";
        await using var dbContext = await dbHub.CreateDbContext(readWrite: true);
        dbContext.Add(new DbWebHookDelivery {
            Id = id,
            WebHookId = hookId.Value,
            Seq = dbHub.VersionGenerator.NextVersion(),
            EventType = "message.posted",
            Payload = "{}",
            Status = status,
            CreatedAt = createdAt.ToDateTime(),
        });
        await dbContext.SaveChangesAsync();
        return id;
    }
}
