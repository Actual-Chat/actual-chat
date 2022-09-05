using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Mathematics.Internal;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.IO;

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
        await base.Initialize(cancellationToken).ConfigureAwait(false);
        var dependencies = InitializeTasks
            .Where(kv => kv.Key.GetType().Name.OrdinalStartsWith("Users"))
            .Select(kv => kv.Value)
            .ToArray();
        await Task.WhenAll(dependencies).ConfigureAwait(false);

        var dbContextFactory = Services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var defaultChatExists = await dbContext.Chats
            .Where(c => c.Id == (string)Constants.Chat.DefaultChatId)
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
