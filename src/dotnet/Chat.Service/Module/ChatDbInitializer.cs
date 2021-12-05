using ActualChat.Audio;
using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.IO;

namespace ActualChat.Chat.Module;

public class ChatDbInitializer : DbInitializer<ChatDbContext>
{
    private static readonly string[] RandomWords =
        { "most", "chat", "actual", "ever", "amazing", "absolutely", "terrific", "truly", "level 100500" };
    private IBlobStorageProvider Blobs { get; }

    public ChatDbInitializer(IServiceProvider services, IBlobStorageProvider blobs) : base(services)
        => Blobs = blobs;

    public override async Task Initialize(CancellationToken cancellationToken)
    {
        await base.Initialize(cancellationToken).ConfigureAwait(false);
        var dependencies = InitializeTasks
            .Where(kv => kv.Key.GetType().Name.StartsWith("Users", StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .ToArray();
        await Task.WhenAll(dependencies).ConfigureAwait(false);

        var dbContextFactory = Services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        if (DbInfo.ShouldRecreateDb) {
            Log.LogInformation("Recreating DB...");
            // Creating "The Actual One" chat
            var defaultChatId = Constants.Chat.DefaultChatId;
            var adminUserId = UserConstants.Admin.UserId;
            var dbChat = new DbChat() {
                Id = defaultChatId,
                Version = VersionGenerator.NextVersion(),
                Title = "The Actual One",
                CreatedAt = Clocks.SystemClock.Now,
                IsPublic = true,
                Owners = {
                        new DbChatOwner() {
                            ChatId = defaultChatId,
                            UserId = adminUserId,
                        },
                    },
            };
            await dbContext.Chats.AddAsync(dbChat, cancellationToken).ConfigureAwait(false);

            var dbAuthor = new DbChatAuthor() {
                Id = DbChatAuthor.ComposeId(defaultChatId, 1),
                ChatId = defaultChatId,
                LocalId = 1,
                Version = VersionGenerator.NextVersion(),
                Name = UserConstants.Admin.Name,
                Picture = UserConstants.Admin.Picture,
                IsAnonymous = false,
                UserId = adminUserId,
            };
            await dbContext.ChatAuthors.AddAsync(dbAuthor, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await AddAudioBlob("0000.webm", "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm", cancellationToken)
                .ConfigureAwait(false);
            await AddAudioBlob("0001.webm", "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm", cancellationToken)
                .ConfigureAwait(false);

            var now = Clocks.SystemClock.Now;
            await AddRandomEntries(dbContext, dbChat, dbAuthor, 0.5, 100, null, cancellationToken).ConfigureAwait(false);
            await AddRandomEntries(dbContext, dbChat, dbAuthor, 1, 4, now, cancellationToken).ConfigureAwait(false);
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
                    let count = thisChatEntries.Count(e1 => e1.Id == e.Id)
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
                        e.CompositeId, e.Id, e.Type, e.Content);
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
        var lastId = await dbContext.ChatEntries
            .Select(e => e.Id)
            .OrderByDescending(v => v)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var lastBeginsAt = beginsAt ?? SystemClock.Now - TimeSpan.FromDays(1);
        var lastEndsAt = lastBeginsAt;
        if (!beginsAt.HasValue && await dbContext.ChatEntries.AnyAsync(cancellationToken).ConfigureAwait(false)) {
            lastBeginsAt = await dbContext.ChatEntries
                    .Select(e => e.BeginsAt)
                    .MaxAsync(cancellationToken).ConfigureAwait(false);
            lastEndsAt = await dbContext.ChatEntries
                    .Select(e => e.EndsAt)
                    .MaxAsync(cancellationToken).ConfigureAwait(false)
                ?? lastBeginsAt;
        }

        var rnd = new Random(1);
        for (var i = 0; i < count; i++) {
            if (i == 0 && !beginsAt.HasValue)
                AddTextEntry("First");
            else if (rnd.NextDouble() <= audioRecordProbability) {
                if (i % 2 == 0)
                    AddAudioEntry1();
                else
                    AddAudioEntry2();
            }
            else
                AddTextEntry();
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        void AddTextEntry(string? content = null)
        {
            lastEndsAt += TimeSpan.FromSeconds(rnd.NextDouble() * 5);
            lastBeginsAt = lastEndsAt;
            var textEntry = new DbChatEntry() {
                CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, ++lastId),
                ChatId = dbChat.Id,
                Id = lastId,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Type = ChatEntryType.Text,
                Content = content ?? GetRandomSentence(rnd, 30),
                AuthorId = dbAuthor.Id,
            };
            dbContext.Add(textEntry);
        }

        void AddAudioEntry1()
        {
            var beginsAt = new Moment(DateTime.Parse("2021-11-05 16:41:18.504314 +00:00", NumberFormatInfo.InvariantInfo));
            var endsAt = new Moment(DateTime.Parse("2021-11-05 16:41:29.543314 +00:00", NumberFormatInfo.InvariantInfo));
            var duration = endsAt - beginsAt;
            lastBeginsAt = Moment.Max(lastBeginsAt, lastEndsAt + TimeSpan.FromSeconds(20 * (rnd.NextDouble() - 0.5)));
            lastEndsAt = lastBeginsAt + duration;

            var textToTimeMap =
                "{\"SourcePoints\":[0,4,18,20,25,27,37,46,53,57,64,74,81,93,98],\"TargetPoints\":[0,1.8,2.4,3.2,3.4,4.2,4.3,5.4,5.5,6.9,7.4,7.6,8.9,9.9,10.5]}";
            var audioEntry = new DbChatEntry {
                CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, ++lastId),
                ChatId = dbChat.Id,
                Id = lastId,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Type = ChatEntryType.Audio,
                Content = "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm",
                AuthorId = dbAuthor.Id,
            };
            dbContext.Add(audioEntry);
            var textEntry = new DbChatEntry {
                CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, ++lastId),
                ChatId = dbChat.Id,
                Id = lastId,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Type = ChatEntryType.Text,
                Content =
                    "Мой друг художник и поэт в Дождливый вечер на стекле мою любовь нарисовал " +
                    "открыв мне чудо на Земле",
                TextToTimeMap = textToTimeMap,
                AudioEntryId = audioEntry.Id,
                AuthorId = dbAuthor.Id,
            };
            dbContext.Add(textEntry);
        }

        void AddAudioEntry2()
        {
            var beginsAt = new Moment(DateTime.Parse("2021-11-05 17:26:05.745700 +00:00", NumberFormatInfo.InvariantInfo));
            var endsAt = new Moment(DateTime.Parse("2021-11-05 17:26:16.275700 +00:00", NumberFormatInfo.InvariantInfo));
            var duration = endsAt - beginsAt;
            lastBeginsAt = Moment.Max(lastBeginsAt, lastEndsAt + TimeSpan.FromSeconds(20 * (rnd.NextDouble() - 0.5)));
            lastEndsAt = lastBeginsAt + duration;

            var textToTimeMap =
                "{\"SourcePoints\":[0,5,31,35,53,63,69,76,82,119,121,126],\"TargetPoints\":[0,1.4,3,3.6,4.8,5.3,6,6.3,7,9.5,9.5,10.53]}";
            var audioEntry = new DbChatEntry {
                CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, ++lastId),
                ChatId = dbChat.Id,
                Id = lastId,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Type = ChatEntryType.Audio,
                Content = "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm",
                AuthorId = dbAuthor.Id,
            };
            dbContext.Add(audioEntry);
            var textEntry = new DbChatEntry {
                CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, ++lastId),
                ChatId = dbChat.Id,
                Id = lastId,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Type = ChatEntryType.Text,
                Content =
                    "утро в декабре туманом окутана под ногами белый снег предатель виден каждый " +
                    "шаг и холоду лютому слишком просто сладить с тобой",
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
        var sourceBlobStream = filePath.ReadBlobStream(cancellationToken);
        var audioLog = Services.LogFor<AudioSource>();
        var audio = new AudioSource(sourceBlobStream, TimeSpan.Zero, audioLog, CancellationToken.None);
        var blobs = Blobs.GetBlobStorage(BlobScope.AudioRecord);
        var audioBlobStream = audio.GetBlobStream(cancellationToken);
        // NOTE(AY): Shouldn't we simply write source blob stream here?
        await blobs.UploadBlobStream(blobId, audioBlobStream, cancellationToken).ConfigureAwait(false);

        static FilePath GetAudioDataDir()
            => new FilePath(Path.GetDirectoryName(typeof(ChatDbInitializer).Assembly.Location)) & "data";
    }

    private static string GetRandomSentence(Random random, int maxLength)
        => Enumerable
            .Range(0, random.Next(maxLength))
            .Select(_ => RandomWords[Random.Shared.Next(RandomWords.Length)])
            .ToDelimitedString(" ")
            .Capitalize();
}
