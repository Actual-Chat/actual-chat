using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class LocalIdGeneratorTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact(Skip = "For manual runs only")]
    public async Task LocalIdsOnDifferentHostsAreUnique()
    {
        using var h1 = await NewAppHost("localId1");
        using var h2 = await NewAppHost("localId2");
        var hub1 = h1.Services.DbHub<ChatDbContext>();
        var hub2 = h1.Services.DbHub<ChatDbContext>();
        var idGenerator1 = h1.Services.GetRequiredService<IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef>>();
        var idGenerator2 = h2.Services.GetRequiredService<IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef>>();

        idGenerator1.Should().NotBeSameAs(idGenerator2);

        var shardRef = new DbChatEntryShardRef(new ChatId("p-9B7NAR-Cdis8n"), ChatEntryKind.Audio);
        var resultTasks = new List<Task<long>>();
        for (int i = 0; i < 200; i++) {
            var next1Task = BackgroundTask.Run(async () => {
                await using var dbContext1 = await hub1.CreateDbContext();
                return await idGenerator1.Next(dbContext1,
                    shardRef,
                    CancellationToken.None);
            });
            resultTasks.Add(next1Task);
            var next2Task = BackgroundTask.Run(async () => {
                await using var dbContext2 = await hub2.CreateDbContext();
                return await idGenerator2.Next(dbContext2,
                    shardRef,
                    CancellationToken.None);
            });
            resultTasks.Add(next2Task);
        }

        var ids = await resultTasks.Collect();
        ids.Distinct().Count().Should().Be(ids.Length);

        // await Task.WhenAll(next1Task, next2Task);

        // var next1 = await next1Task;
        // var next2 = await next2Task;
        //
        // next1.Should().NotBe(next2);
    }
}
