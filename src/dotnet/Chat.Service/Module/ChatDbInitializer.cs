using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Mathematics.Internal;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.IO;

namespace ActualChat.Chat.Module;

public class ChatDbInitializer : DbInitializer<ChatDbContext>
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
        var shouldRecreateDb = HostInfo.IsDevelopmentInstance && (DbInfo.ShouldRecreateDb || !defaultChatExists);

        if (shouldRecreateDb) {
            Log.LogInformation("Recreating DB...");
            // Creating "The Actual One" chat
            var defaultChatId = Constants.Chat.DefaultChatId;
            var adminUserId = UserConstants.Admin.UserId;
            var dbChat = new DbChat {
                Id = defaultChatId,
                Version = VersionGenerator.NextVersion(),
                Title = "The Actual One",
                CreatedAt = Clocks.SystemClock.Now,
                IsPublic = true,
                Owners = {
                    new DbChatOwner {
                        DbChatId = defaultChatId,
                        DbUserId = adminUserId,
                    },
                },
            };
            dbContext.Chats.Add(dbChat);

            var dbAuthor = new DbChatAuthor {
                Id = DbChatAuthor.ComposeId(defaultChatId, 1),
                ChatId = defaultChatId,
                LocalId = 1,
                Version = VersionGenerator.NextVersion(),
                Name = UserConstants.Admin.Name,
                IsAnonymous = false,
                UserId = adminUserId,
            };
            dbContext.ChatAuthors.Add(dbAuthor);

            try {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException) {
                // Looks like we're starting w/ existing DB
                dbContext.ChangeTracker.Clear();
            }

            await AddChatAuthors(dbContext, cancellationToken).ConfigureAwait(false);

            await AddAudioBlob("0000.webm", "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm", cancellationToken)
                .ConfigureAwait(false);
            await AddAudioBlob("0001.webm", "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm", cancellationToken)
                .ConfigureAwait(false);

            var now = Clocks.SystemClock.Now;
            await AddRandomEntries(dbContext,
                    dbChat,
                    dbAuthor,
                    0.1,
                    2000,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            // await AddRandomEntries(dbContext, dbChat, dbAuthor, 1, 4, now, cancellationToken).ConfigureAwait(false);

            await UpgradeChats(dbContext, cancellationToken).ConfigureAwait(false);
        }
        else if (DbInfo.ShouldMigrateDb) {
            // Post-migration upgrades
            await UpgradeChats(dbContext, cancellationToken).ConfigureAwait(false);
        }

        if (DbInfo.ShouldVerifyDb) {
            Log.LogInformation("Verifying DB...");
            var chatIds = await dbContext.Chats
                .Select(c => c.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var chatId in chatIds) {
                var thisChatEntries = dbContext.ChatEntries.Where(e => e.ChatId == chatId);
                var duplicateEntries = await (
                    from e in thisChatEntries
                    let count = thisChatEntries.Count(e1 => e1.Id == e.Id && e1.Type == e.Type)
                    where count > 1
                    select e
                    ).ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (duplicateEntries.Count <= 0)
                    continue;

                Log.LogCritical("Duplicate entries in Chat #{ChatId}:", chatId);
                foreach (var e in duplicateEntries)
                    Log.LogCritical(
                        "- Entry w/ CompositeId = {CompositeId}, Id = {Id}, Type = {Type}, '{Content}'",
                        e.CompositeId,
                        e.Id,
                        e.Type,
                        e.Content);
            }
        }
    }

    private async Task UpgradeChats(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        var candidateChatIds = await dbContext.Chats
            .Where(c => c.Owners.Any())
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (candidateChatIds.Count == 0) {
            Log.LogInformation("No chats to upgrade");
            return;
        }

        try {
            Log.LogInformation("Upgrading {ChatCount} chats...", candidateChatIds.Count);
            foreach (var chatId in candidateChatIds) {
                var cmd = new IChatsBackend.UpgradeChatCommand(chatId);
                await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
            }
            Log.LogInformation("Chats are upgraded");
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to upgrade chats!");
            throw;
        }
    }

    private async Task AddChatAuthors(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        var defaultChatId = Constants.Chat.DefaultChatId;
        for (int i = 1; i < 30; i++) {
            var dbAuthor = new DbChatAuthor {
                Id = DbChatAuthor.ComposeId(defaultChatId, i + 1),
                ChatId = defaultChatId,
                LocalId = i + 1,
                Version = VersionGenerator.NextVersion(),
                Name = $"User_{i}",
                IsAnonymous = false,
                UserId = $"user{i}",
            };
            dbContext.ChatAuthors.Add(dbAuthor);
            try {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException) {
                // Looks like we're starting w/ existing DB
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private async Task AddRandomEntries(
        ChatDbContext dbContext,
        DbChat dbChat,
        DbChatAuthor dbAuthor,
        double audioRecordProbability,
        int count,
        Moment? beginsAt,
        CancellationToken cancellationToken)
    {
        var chats = (ChatsBackend)Services.GetRequiredService<IChatsBackend>();
        var lastBeginsAt = beginsAt ?? SystemClock.Now - TimeSpan.FromDays(1);
        var lastEndsAt = lastBeginsAt;
        if (!beginsAt.HasValue && await dbContext.ChatEntries.AnyAsync(cancellationToken).ConfigureAwait(false)) {
            lastBeginsAt = await dbContext.ChatEntries
                .Select(e => e.BeginsAt)
                .MaxAsync(cancellationToken)
                .ConfigureAwait(false);
            lastEndsAt = await dbContext.ChatEntries
                    .Select(e => e.EndsAt)
                    .MaxAsync(cancellationToken)
                    .ConfigureAwait(false)
                ?? lastBeginsAt;
        }

        var rnd = new Random(1);
        for (var i = 0; i < count; i++)
            if (i == 0 && !beginsAt.HasValue)
                await AddTextEntry("First").ConfigureAwait(false);
            else if (rnd.NextDouble() <= audioRecordProbability) {
                if (i % 2 == 0)
                    await AddAudioEntry1().ConfigureAwait(false);
                else
                    await AddAudioEntry2().ConfigureAwait(false);
            }
            else
                await AddTextEntry().ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        async Task AddTextEntry(string? content = null)
        {
            lastEndsAt += TimeSpan.FromSeconds(rnd.NextDouble() * 5);
            lastBeginsAt = lastEndsAt;
            var id = await chats
                .DbNextEntryId(dbContext, dbChat.Id, ChatEntryType.Text, cancellationToken)
                .ConfigureAwait(false);
            var textEntry = new DbChatEntry {
                CompositeId = DbChatEntry.ComposeId(dbChat.Id, ChatEntryType.Text, id),
                Type = ChatEntryType.Text,
                ChatId = dbChat.Id,
                Id = id,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Content = $"{id} {content ?? GetRandomSentence(rnd, 30)}",
                AuthorId = dbAuthor.Id,
            };
            dbContext.Add(textEntry);
        }

        async Task AddAudioEntry1()
        {
            var originalBeginsAt =
                new Moment(DateTime.Parse("2021-11-05 16:41:18.504314 +00:00", NumberFormatInfo.InvariantInfo));
            var originalEndsAt =
                new Moment(DateTime.Parse("2021-11-05 16:41:29.543314 +00:00", NumberFormatInfo.InvariantInfo));
            var duration = originalEndsAt - originalBeginsAt;
            lastBeginsAt = Moment.Max(lastBeginsAt, lastEndsAt + TimeSpan.FromSeconds(20 * (rnd.NextDouble() - 0.5)));
            lastEndsAt = lastBeginsAt + duration;

            var id = await chats
                .DbNextEntryId(dbContext, dbChat.Id, ChatEntryType.Audio, cancellationToken)
                .ConfigureAwait(false);
            var textToTimeMap = ConvertOldTextToTimeMap(
                "{\"SourcePoints\":[0,4,18,20,25,27,37,46,53,57,64,74,81,93,98],\"TargetPoints\":[0,1.8,2.4,3.2,3.4,4.2,4.3,5.4,5.5,6.9,7.4,7.6,8.9,9.9,10.5]}");
            var audioEntry = new DbChatEntry {
                CompositeId = DbChatEntry.ComposeId(dbChat.Id, ChatEntryType.Audio, id),
                ChatId = dbChat.Id,
                Type = ChatEntryType.Audio,
                Id = id,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Content = "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm",
                AuthorId = dbAuthor.Id,
            };
            dbContext.Add(audioEntry);

            id = await chats
                .DbNextEntryId(dbContext, dbChat.Id, ChatEntryType.Text, cancellationToken)
                .ConfigureAwait(false);
            var textEntry = new DbChatEntry {
                CompositeId = DbChatEntry.ComposeId(dbChat.Id, ChatEntryType.Text, id),
                ChatId = dbChat.Id,
                Type = ChatEntryType.Text,
                Id = id,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Content =
                    "?????? ???????? ???????????????? ?? ???????? ?? ?????????????????? ?????????? ???? ???????????? ?????? ???????????? ?????????????????? "
                    + "???????????? ?????? ???????? ???? ??????????",
                TextToTimeMap = textToTimeMap,
                AudioEntryId = audioEntry.Id,
                AuthorId = dbAuthor.Id,
            };
            dbContext.Add(textEntry);
        }

        async Task AddAudioEntry2()
        {
            var originalBeginsAt =
                new Moment(DateTime.Parse("2021-11-05 17:26:05.745700 +00:00", NumberFormatInfo.InvariantInfo));
            var originalEndsAt =
                new Moment(DateTime.Parse("2021-11-05 17:26:16.275700 +00:00", NumberFormatInfo.InvariantInfo));
            var duration = originalEndsAt - originalBeginsAt;
            lastBeginsAt = Moment.Max(lastBeginsAt, lastEndsAt + TimeSpan.FromSeconds(20 * (rnd.NextDouble() - 0.5)));
            lastEndsAt = lastBeginsAt + duration;

            var textToTimeMap = ConvertOldTextToTimeMap(
                "{\"SourcePoints\":[0,5,31,35,53,63,69,76,82,119,121,126],\"TargetPoints\":[0,1.4,3,3.6,4.8,5.3,6,6.3,7,9.5,9.5,10.53]}");
            var id = await chats
                .DbNextEntryId(dbContext, dbChat.Id, ChatEntryType.Audio, cancellationToken)
                .ConfigureAwait(false);
            var audioEntry = new DbChatEntry {
                CompositeId = DbChatEntry.ComposeId(dbChat.Id, ChatEntryType.Audio, id),
                ChatId = dbChat.Id,
                Type = ChatEntryType.Audio,
                Id = id,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Content = "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm",
                AuthorId = dbAuthor.Id,
            };
            dbContext.Add(audioEntry);

            id = await chats
                .DbNextEntryId(dbContext, dbChat.Id, ChatEntryType.Text, cancellationToken)
                .ConfigureAwait(false);
            var textEntry = new DbChatEntry {
                CompositeId = DbChatEntry.ComposeId(dbChat.Id, ChatEntryType.Text, id),
                ChatId = dbChat.Id,
                Type = ChatEntryType.Text,
                Id = id,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Content =
                    "???????? ?? ?????????????? ?????????????? ?????????????? ?????? ???????????? ?????????? ???????? ?????????????????? ?????????? ???????????? "
                    + "?????? ?? ???????????? ???????????? ?????????????? ???????????? ?????????????? ?? ??????????",
                TextToTimeMap = textToTimeMap,
                AudioEntryId = audioEntry.Id,
                AuthorId = dbAuthor.Id,
            };
            dbContext.Add(textEntry);
        }
    }

    private async Task AddAudioBlob(
        FilePath fileName,
        string blobId,
        CancellationToken cancellationToken)
    {
        var filePath = GetAudioDataDir() & fileName;
        var sourceBlobStream = filePath.ReadByteStream(1024, cancellationToken).Memoize();
        var blobs = Blobs.GetBlobStorage(BlobScope.AudioRecord);
        await blobs.UploadByteStream(blobId, sourceBlobStream.Replay(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        static FilePath GetAudioDataDir()
        {
            return new FilePath(Path.GetDirectoryName(typeof(ChatDbInitializer).Assembly.Location)) & "data";
        }
    }

    private static string GetRandomSentence(Random random, int maxLength)
        => Enumerable
            .Range(0, random.Next(maxLength))
            .Select(_ => RandomWords[Random.Shared.Next(RandomWords.Length)])
            .ToDelimitedString(" ")
            .Capitalize();

    private static string ConvertOldTextToTimeMap(string textToTimeMapJson)
    {
        if (!textToTimeMapJson.StartsWith("{\"SourcePoints\"", StringComparison.InvariantCultureIgnoreCase))
            return textToTimeMapJson;

        var oldMap = NewtonsoftJsonSerialized.New<OldLinearMap>(textToTimeMapJson).Value;
        var newMap = oldMap.ToLinearMap();
        textToTimeMapJson = NewtonsoftJsonSerialized.New(newMap).Data;
        return textToTimeMapJson;
    }
}
