using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Mathematics.Internal;
using Microsoft.EntityFrameworkCore;
using Stl.IO;

namespace ActualChat.Chat.Module;

public partial class ChatDbInitializer : DbInitializer<ChatDbContext>
{
    private async Task Generate(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        Log.LogInformation("Generating initial DB content...");

        // Creating "The Actual One" chat
        var defaultChatId = Constants.Chat.DefaultChatId;
        var adminUserId = Constants.User.Admin.UserId;
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

        var dbAuthor = new DbAuthor {
            Id = new AuthorId(defaultChatId, 1, AssumeValid.Option),
            ChatId = defaultChatId,
            LocalId = 1,
            Version = VersionGenerator.NextVersion(),
            IsAnonymous = false,
            UserId = adminUserId,
        };
        dbContext.Authors.Add(dbAuthor);

        try {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException) {
            // Looks like we're starting w/ existing DB
            dbContext.ChangeTracker.Clear();
        }

        await AddAuthors(dbContext, cancellationToken).ConfigureAwait(false);

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

        // TODO(AY): Remove this once logic above is upgraded to create chats properly
        await UpgradeChats(dbContext, cancellationToken).ConfigureAwait(false);
        await UpgradePermissions(dbContext, cancellationToken).ConfigureAwait(false);
        await EnsureAnnouncementsChatExists(dbContext, cancellationToken).ConfigureAwait(false);
    }

    private async Task AddAuthors(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        var chatId = Constants.Chat.DefaultChatId;
        for (int i = 1; i < 30; i++) {
            var dbAuthor = new DbAuthor {
                Id = new AuthorId(chatId, i + 1, AssumeValid.Option),
                ChatId = chatId,
                LocalId = i + 1,
                Version = VersionGenerator.NextVersion(),
                IsAnonymous = false,
                UserId = $"user{i:00}",
            };
            dbContext.Authors.Add(dbAuthor);
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
        DbAuthor dbAuthor,
        double audioRecordProbability,
        int count,
        Moment? beginsAt,
        CancellationToken cancellationToken)
    {
        var chatId = new ChatId(dbChat.Id);
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
            var localId = await chats
                .DbNextLocalId(dbContext, chatId, ChatEntryKind.Text, cancellationToken)
                .ConfigureAwait(false);
            var id = new ChatEntryId(chatId, ChatEntryKind.Text, localId, AssumeValid.Option);
            var entry = new DbChatEntry {
                Id = id,
                Kind = ChatEntryKind.Text,
                ChatId = dbChat.Id,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Content = $"{id} {content ?? GetRandomSentence(rnd, 30)}",
                AuthorId = dbAuthor.Id,
            };
            dbContext.Add(entry);
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

            var localId = await chats
                .DbNextLocalId(dbContext, chatId, ChatEntryKind.Audio, cancellationToken)
                .ConfigureAwait(false);
            var id = new ChatEntryId(chatId, ChatEntryKind.Audio, localId, AssumeValid.Option);
            var textToTimeMap = ConvertOldTextToTimeMap(
                "{\"SourcePoints\":[0,4,18,20,25,27,37,46,53,57,64,74,81,93,98],\"TargetPoints\":[0,1.8,2.4,3.2,3.4,4.2,4.3,5.4,5.5,6.9,7.4,7.6,8.9,9.9,10.5]}");
            var audioEntry = new DbChatEntry {
                Id = id,
                ChatId = dbChat.Id,
                Kind = ChatEntryKind.Audio,
                LocalId = localId,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Content = "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm",
                AuthorId = dbAuthor.Id,
            };
            dbContext.Add(audioEntry);

            localId = await chats
                .DbNextLocalId(dbContext, chatId, ChatEntryKind.Text, cancellationToken)
                .ConfigureAwait(false);
            id = new ChatEntryId(chatId, ChatEntryKind.Text, localId, AssumeValid.Option);
            var textEntry = new DbChatEntry {
                Id = id,
                ChatId = dbChat.Id,
                Kind = ChatEntryKind.Text,
                LocalId = localId,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Content =
                    "Мой друг художник и поэт в Дождливый вечер на стекле мою любовь нарисовал "
                    + "открыв мне чудо на Земле",
                TextToTimeMap = textToTimeMap,
                AudioEntryId = audioEntry.LocalId,
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
            var localId = await chats
                .DbNextLocalId(dbContext, chatId, ChatEntryKind.Audio, cancellationToken)
                .ConfigureAwait(false);
            var id = new ChatEntryId(chatId, ChatEntryKind.Audio, localId, AssumeValid.Option);
            var audioEntry = new DbChatEntry {
                Id = id,
                ChatId = dbChat.Id,
                Kind = ChatEntryKind.Audio,
                LocalId = localId,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Content = "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm",
                AuthorId = dbAuthor.Id,
            };
            dbContext.Add(audioEntry);

            localId = await chats
                .DbNextLocalId(dbContext, chatId, ChatEntryKind.Text, cancellationToken)
                .ConfigureAwait(false);
            id = new ChatEntryId(chatId, ChatEntryKind.Text, localId, AssumeValid.Option);
            var textEntry = new DbChatEntry {
                Id = id,
                ChatId = dbChat.Id,
                Kind = ChatEntryKind.Text,
                LocalId = localId,
                Version = VersionGenerator.NextVersion(),
                BeginsAt = lastBeginsAt,
                EndsAt = lastEndsAt,
                Content =
                    "утро в декабре туманом окутана под ногами белый снег предатель виден каждый "
                    + "шаг и холоду лютому слишком просто сладить с тобой",
                TextToTimeMap = textToTimeMap,
                AudioEntryId = audioEntry.LocalId,
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
        if (!textToTimeMapJson.OrdinalIgnoreCaseStartsWith("{\"SourcePoints\""))
            return textToTimeMapJson;

        var oldMap = NewtonsoftJsonSerialized.New<OldLinearMap>(textToTimeMapJson).Value;
        var newMap = oldMap.ToLinearMap();
        textToTimeMapJson = NewtonsoftJsonSerialized.New(newMap).Data;
        return textToTimeMapJson;
    }
}
