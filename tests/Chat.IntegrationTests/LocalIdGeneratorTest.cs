using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Testing.Host;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection)), Trait("Category", nameof(ChatCollection))]
public class LocalIdGeneratorTest(AppHostFixture fixture, ITestOutputHelper @out)
{
    private TestAppHost Host => fixture.Host;
    private ITestOutputHelper Out { get; } = fixture.Host.UseOutput(@out);

    [Fact(Skip = "For manual runs only")]
    public async Task LocalIdsOnDifferentHostsAreUnique()
    {
        using var appHost1 = await NewAppHost();
        using var appHost2 = await NewAppHost();
        var hub1 = appHost1.Services.DbHub<ChatDbContext>();
        var hub2 = appHost1.Services.DbHub<ChatDbContext>();
        var idGenerator1 = appHost1.Services.GetRequiredService<IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef>>();
        var idGenerator2 = appHost2.Services.GetRequiredService<IDbShardLocalIdGenerator<DbChatEntry, DbChatEntryShardRef>>();

        idGenerator1.Should().NotBeSameAs(idGenerator2);

        var shardRef = new DbChatEntryShardRef(new ChatId("p-9B7NAR-Cdis8n"), ChatEntryKind.Audio);
        var resultTasks = new List<Task<long>>();
        for (int i = 0; i < 200; i++) {
            var next1Task = BackgroundTask.Run(async () => {
                var dbContext1 = hub1.CreateDbContext();
                await using var __1 = dbContext1.ConfigureAwait(false);
                return await idGenerator1.Next(dbContext1,
                    shardRef,
                    CancellationToken.None);
            });
            resultTasks.Add(next1Task);
            var next2Task = BackgroundTask.Run(async () => {
                var dbContext2 = hub2.CreateDbContext();
                await using var __2 = dbContext2.ConfigureAwait(false);
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

    private Task<TestAppHost> NewAppHost(TestAppHostOptions? options = default)
        => TestAppHostFactory.NewAppHost(Out, options);
}
