using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Module;

public partial class ChatDbInitializer : DbInitializer<ChatDbContext>
{
    private static readonly string[] RandomWords =
        { "most", "chat", "actual", "ever", "amazing", "absolutely", "terrific", "truly", "level 100500" };

    private IBlobStorageProvider Blobs { get; }
    private HostInfo HostInfo { get; init; } = null!;

    public ChatDbInitializer(
        IServiceProvider services,
        IBlobStorageProvider blobs,
        HostInfo hostInfo) : base(services)
    {
        Blobs = blobs;
        HostInfo = hostInfo;
    }

    public override async Task Initialize(CancellationToken cancellationToken)
    {
        var dependencies = (
            from kv in InitializeTasks
            let dbInitializer = kv.Key
            let dbInitializerName = dbInitializer.GetType().Name
            let task = kv.Value
            where OrdinalEquals(dbInitializerName, "UsersDbInitializer")
            select task
            ).ToArray();
        await Task.WhenAll(dependencies).ConfigureAwait(false);
        await base.Initialize(cancellationToken).ConfigureAwait(false);

        var dbContextFactory = Services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var defaultChatExists = await dbContext.Chats
            .Where(c => c.Id == Constants.Chat.DefaultChatId)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
        var isNewDb = HostInfo.IsDevelopmentInstance && (DbInfo.ShouldRecreateDb || !defaultChatExists);

        if (isNewDb)
            await Generate(dbContext, cancellationToken).ConfigureAwait(false);
        if (DbInfo.ShouldMigrateDb)
            await Upgrade(dbContext, cancellationToken).ConfigureAwait(false);
        if (DbInfo.ShouldVerifyDb)
            await Verify(dbContext, cancellationToken).ConfigureAwait(false);
    }
}
