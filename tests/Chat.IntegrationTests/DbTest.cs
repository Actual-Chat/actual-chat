using System.Diagnostics;
using ActualChat.Chat.Db;
using ActualChat.Testing.Host;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Mathematics;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection)), Trait("Category", nameof(ChatCollection))]
public class DbTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private ChatId TestChatId => Constants.Chat.DefaultChatId;

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(2000)]
    public async Task ConcurrentUpdatesShouldNotBlockReads(int duration)
    {
        using var appHost = await Fixture.NewHost();
        var logger = appHost.Services.LogFor<DbTest>();
        logger.LogInformation("app host init");

        var chatId = TestChatId;
        var entryKind = ChatEntryKind.Text;
        var range = new Range<long>(1500, 1601);

        var dbHub = appHost.Services.GetRequiredService<DbHub<ChatDbContext>>();

        var dbContext = dbHub.CreateDbContext(true);
        await using var __ = dbContext.ConfigureAwait(false);
        var q = dbContext.Database.SqlQuery<string>(FormattableStringFactory.Create("SHOW max_connections;"));
        var maxConnections = q.AsEnumerable().FirstOrDefault();
        logger.LogInformation("max_connections='{MaxConnections}'", maxConnections);

        var writeTasks = new List<Task<TimeSpan>>();

        var usedLocalIds = new List<long>();
        for (var i = 0; i < 20; i++) {
            var localI = i;
            long localId;
            while (true) {
                localId = range.Start + Random.Shared.Next( (int)(range.End - range.Start));
                if (usedLocalIds.Contains(localId))
                    continue;
                usedLocalIds.Add(localId);
                break;
            }

            var entryId = new ChatEntryId(chatId, entryKind, localId, AssumeValid.Option);
            var task = Task.Run(() => MeasureDuration(
                () => UpdateChatEntry(dbHub, entryId, TimeSpan.FromMilliseconds(duration), default),
                 logger, $"UpdateChatEntry#{localI}({localId})"));
            writeTasks.Add(task);
        }

        await Task.Delay(200);

        var readTasks = new List<Task<TimeSpan>>();
        for (var i = 0; i < 10; i++) {
            var localI = i;
            var readTask = MeasureDuration(
                () => ReadChatEntries(dbHub, chatId, entryKind, range, default),
                logger, $"ReadChatEntries#{localI}");
            readTasks.Add(readTask);
        }
        await Task.WhenAll(readTasks.ToArray());
        foreach (var readTask in readTasks) {
            var elapsed = await readTask;
            elapsed.TotalMilliseconds.Should().BeLessThan(500, "reads should not be blocked with updates");
        }

        logger.LogInformation("Completed reads");
        await Task.WhenAll(writeTasks.ToArray());
        logger.LogInformation("Completed test");
    }

    [Fact]
    public async Task DeadlockShouldBeDetected()
    {
        using var appHost = await Fixture.NewHost();
        var logger = appHost.Services.LogFor<DbTest>();
        logger.LogInformation("app host init");

        var chatId = TestChatId;
        var entryKind = ChatEntryKind.Text;
        var localId1 = 1500;
        var localId2 = 1600;

        var dbHub = appHost.Services.GetRequiredService<DbHub<ChatDbContext>>();

        var task1 = Task.Run(() => ProvokeDeadlock(dbHub, chatId, entryKind, localId1, localId2, default));

        var task2 = Task.Run(() => ProvokeDeadlock(dbHub, chatId, entryKind, localId2, localId1, default));

        Exception? exception = null;
        try {
            await Task.WhenAll(task1, task2);
        }
        catch (Exception ex) {
            exception = ex;
        }
        var postgresException = exception.Flatten().FirstOrDefault(c => c is Npgsql.PostgresException);
        postgresException.Should().NotBeNull();
        postgresException!.Message.Should().Contain("deadlock");

        logger.LogInformation("Completed test");
    }

    private async Task<TimeSpan> MeasureDuration(Func<Task> taskFactory, ILogger logger, string? taskDescription = null)
    {
        taskDescription ??= "Unknown";
        logger.LogInformation("Task '{TaskDescription}' about to start", taskDescription);
        var sw = Stopwatch.StartNew();
        await taskFactory();
        sw.Stop();
        logger.LogInformation("Task '{TaskDescription}' took {Elapsed}ms", taskDescription, sw.ElapsedMilliseconds);
        return sw.Elapsed;
    }

    private static async Task<ApiArray<ChatEntry>> ReadChatEntries(
        DbHub<ChatDbContext> dbHub,
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long> range,
        CancellationToken cancellationToken)
    {
        var dbContext = dbHub.CreateDbContext();
        await using var __ = dbContext.ConfigureAwait(false);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var dbEntries = await dbContext.ChatEntries
            .Where(e => e.ChatId == chatId
                && e.Kind == entryKind
                && e.LocalId >= range.Start
                && e.LocalId < range.End)
            .OrderBy(e => e.LocalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken);
        var entries = dbEntries
            .Select(e => e.ToModel(null))
            .ToApiArray();
        return entries;
    }

    private static async Task<ChatEntry> UpdateChatEntry(
        DbHub<ChatDbContext> dbHub,
        ChatEntryId entryId,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        var dbContext = dbHub.CreateDbContext(true);
        await using var __ = dbContext.ConfigureAwait(false);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var dbEntry = await dbContext.ChatEntries
            .ForUpdate()
            .Where(e => e.Id == entryId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        dbEntry.Require();

        const string suffix = ".Suffix";
        var content = dbEntry.Content;
        if (content.Length < 200)
            content += suffix;
        else
            content = content.Replace(suffix, "", StringComparison.OrdinalIgnoreCase);
        dbEntry.Content = content;
        dbEntry.Version = dbHub.VersionGenerator.NextVersion(dbEntry.Version);

        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbEntry.ToModel();
    }

    private static async Task<Unit> ProvokeDeadlock(
        DbHub<ChatDbContext> dbHub,
        ChatId chatId,
        ChatEntryKind entryKind,
        long entry1LocalId,
        long entry2LocalId,
        CancellationToken cancellationToken)
    {
        var dbContext = dbHub.CreateDbContext(true);
        await using var __ = dbContext.ConfigureAwait(false);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var entry1Id = new ChatEntryId(chatId, entryKind, entry1LocalId, AssumeValid.Option);
        var dbEntry1 = await dbContext.ChatEntries
            .ForUpdate()
            .Where(e => e.Id == entry1Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        dbEntry1.Require();

        await Task.Delay(1000, cancellationToken);

        var entry2Id = new ChatEntryId(chatId, entryKind, entry2LocalId, AssumeValid.Option);
        var dbEntry2 = await dbContext.ChatEntries
            .ForUpdate()
            .Where(e => e.Id == entry2Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        dbEntry2.Require();

        await transaction.CommitAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Unit.Default;
    }
}
