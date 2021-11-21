using System.Buffers;
using ActualChat.Audio;
using ActualChat.Blobs;
using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.IO;

namespace ActualChat.Chat.Module;

public class ChatDbInitializer : DbInitializer<ChatDbContext>
{
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

        if (ShouldRecreateDb) {
            // Creating "The Actual One" chat
            var defaultChatId = ChatConstants.DefaultChatId;
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

            // Uncomment this if you need just random text messages
            // await AddRandomTextMessages(dbContext, dbChat, dbAuthor, cancellationToken).ConfigureAwait(false);

            // Uncomment this if you need initial audio and text data
            await AddAudioRecords(dbContext, dbChat, dbAuthor, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AddAudioRecords(
        DbContext dbContext,
        DbChat dbChat,
        DbChatAuthor dbAuthor,
        CancellationToken cancellationToken)
    {
        var lastId = 0;
        var audioEntry = new DbChatEntry {
            CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, ++lastId),
            AuthorId = dbAuthor.Id,
            AudioEntryId = null,
            BeginsAt = new Moment(DateTime.Parse("2021-11-05 16:41:18.504314 +00:00",
                NumberFormatInfo.InvariantInfo)),
            ChatId = dbChat.Id,
            Content = "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm",
            EndsAt = new Moment(DateTime.Parse("2021-11-05 16:41:29.543314 +00:00",
                NumberFormatInfo.InvariantInfo)),
            Id = lastId,
            Type = ChatEntryType.Audio,
            Version = 16359216898269180,
        };
        dbContext.Add(audioEntry);

        var textToTimeMap =
            "{\"SourcePoints\":[0,4,18,20,25,27,37,46,53,57,64,74,81,93,98],\"TargetPoints\":[0,1.8,2.4,3.2,3.4,4.2,4.3,5.4,5.5,6.9,7.4,7.6,8.9,9.9,10.5]}";
        var textEntry = new DbChatEntry {
            CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, ++lastId),
            AuthorId = dbAuthor.Id,
            AudioEntryId = audioEntry.Id,
            BeginsAt = new Moment(DateTime.Parse("2021-11-05 16:41:18.504314 +00:00",
                NumberFormatInfo.InvariantInfo)),
            ChatId = dbChat.Id,
            Content =
                "Мой друг художник и поэт в Дождливый вечер на стекле мою любовь нарисовал открыв мне чудо на Земле",
            EndsAt = new Moment(DateTime.Parse("2021-11-05 16:41:29.004314 +00:00",
                NumberFormatInfo.InvariantInfo)),
            Id = lastId,
            TextToTimeMap = textToTimeMap,
            Type = ChatEntryType.Text,
            Version = 16359216898897618,
        };
        dbContext.Add(textEntry);

        audioEntry = new DbChatEntry {
            CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, ++lastId),
            AuthorId = dbAuthor.Id,
            AudioEntryId = null,
            BeginsAt = new Moment(DateTime.Parse("2021-11-05 17:26:05.671804 +00:00",
                NumberFormatInfo.InvariantInfo)),
            ChatId = dbChat.Id,
            Content = "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm",
            EndsAt = new Moment(DateTime.Parse("2021-11-05 17:26:16.710804 +00:00",
                NumberFormatInfo.InvariantInfo)),
            Id = lastId,
            Type = ChatEntryType.Audio,
            Version = 16361331765465404,
        };
        dbContext.Add(audioEntry);

        textToTimeMap =
            "{\"SourcePoints\":[0,5,31,35,53,63,69,76,82,119,121,126],\"TargetPoints\":[0,1.4,3,3.6,4.8,5.3,6,6.3,7,9.5,9.5,10.53]}";
        textEntry = new DbChatEntry {
            CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, ++lastId),
            AuthorId = dbAuthor.Id,
            AudioEntryId = audioEntry.Id,
            BeginsAt = new Moment(DateTime.Parse("2021-11-05 17:26:05.745700 +00:00",
                NumberFormatInfo.InvariantInfo)),
            ChatId = dbChat.Id,
            Content =
                "утро в декабре туманом окутана под ногами белый снег предатель виден каждый " +
                "шаг и холоду лютому слишком просто сладить с тобой",
            EndsAt = new Moment(DateTime.Parse("2021-11-05 17:26:16.275700 +00:00",
                NumberFormatInfo.InvariantInfo)),
            Id = lastId,
            TextToTimeMap = textToTimeMap,
            Type = ChatEntryType.Text,
            Version = 16361331767501582,
        };
        dbContext.Add(textEntry);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await AddAudioBlob("0000.webm", "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm", cancellationToken)
            .ConfigureAwait(false);
        await AddAudioBlob("0001.webm", "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AddAudioBlob(
        FilePath fileName,
        string blobId,
        CancellationToken cancellationToken)
    {
        var filePath = GetAudioDataDir() & fileName;
        if (!File.Exists(filePath)) {
            throw new FileNotFoundException($"Path for {fileName} data not found", filePath.ToString());
        }
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

    private async Task AddRandomTextMessages(
        DbContext dbContext,
        DbChat dbChat,
        DbChatAuthor dbAuthor,
        CancellationToken cancellationToken)
    {
        var words = new[] { "most", "chat", "actual", "ever", "amazing", "absolutely" };
        for (var id = 10; id < 500; id++) {
            var dbChatEntry = new DbChatEntry() {
                CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, id),
                ChatId = dbChat.Id,
                Id = id,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = Clocks.SystemClock.Now,
                EndsAt = Clocks.SystemClock.Now,
                Type = ChatEntryType.Text,
                Content = GetRandomSentence(30),
                AuthorId = dbAuthor.Id,
            };
            if (id == 0)
                dbChatEntry.Content = "First";
            dbContext.Add(dbChatEntry);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        string GetRandomSentence(int maxLength)
            => Enumerable
                .Range(0, Random.Shared.Next(maxLength))
                .Select(_ => words![Random.Shared.Next(words.Length)])
                .ToDelimitedString(" ");
    }
}
