using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Mathematics.Internal;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.Authentication.Commands;
using Stl.IO;

namespace ActualChat.Chat.Module;

public partial class ChatDbInitializer
{
    protected override async Task InitializeData(CancellationToken cancellationToken)
    {
        // This initializer runs after everything else
        var dependencies = (
            from kv in InitializeTasks
            let dbInitializer = kv.Key
            // let dbInitializerName = dbInitializer.GetType().Name
            let task = kv.Value
            where dbInitializer != this
            select task
            ).ToArray();
        await Task.WhenAll(dependencies).ConfigureAwait(false);

        Log.LogInformation("Initializing data...");

        var dbContext = DbHub.CreateDbContext(true);
        await using var _ = dbContext.ConfigureAwait(false);

        await EnsureAnnouncementsChatExists(dbContext, cancellationToken).ConfigureAwait(false);
        if (HostInfo.IsDevelopmentInstance)
            await EnsureDefaultChatExists(dbContext, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAnnouncementsChatExists(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        var chatId = Constants.Chat.AnnouncementsChatId;
        if (await dbContext.Chats.AnyAsync(c => c.Id == chatId, cancellationToken).ConfigureAwait(false))
            return;

        try {
            Log.LogInformation("There is no 'Announcements' chat, creating one");
            var command = new IChatsUpgradeBackend.CreateAnnouncementsChatCommand();
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("'Announcements' chat is created");
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to create 'Announcements' chat!");
            throw;
        }
    }

    private async Task EnsureDefaultChatExists(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        var chatId = Constants.Chat.DefaultChatId;
        if (await dbContext.Chats.AnyAsync(c => c.Id == chatId, cancellationToken).ConfigureAwait(false))
            return;

        try {
            Log.LogInformation("There is no default chat, creating one");

            var accountsBackend = Services.GetRequiredService<IAccountsBackend>();
            var admin = await accountsBackend.Get(Constants.User.Admin.UserId, cancellationToken)
                .Require()
                .ConfigureAwait(false);

            var command = new IChatsBackend.ChangeCommand(chatId, null, new() {
                Create = new ChatDiff {
                    Title = "The Actual One",
                    IsPublic = true,
                },
            }, admin.Id);
            var chat = await Commander.Call(command, true, cancellationToken).ConfigureAwait(false);

            var dbChat = await dbContext.Chats
                .Get(chat.Id, cancellationToken)
                .Require()
                .ConfigureAwait(false);
            var dbAdminAuthor = await dbContext.Authors
                .SingleAsync(a => a.ChatId == chat.Id && a.UserId == admin.Id, cancellationToken)
                .ConfigureAwait(false);

            await AddAuthors(dbContext, cancellationToken).ConfigureAwait(false);

            await AddAudioBlob("0000.webm", "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm", cancellationToken)
                .ConfigureAwait(false);
            await AddAudioBlob("0001.webm", "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm", cancellationToken)
                .ConfigureAwait(false);

            var now = Clocks.SystemClock.Now;
            await AddRandomEntries(dbContext,
                    dbChat,
                    dbAdminAuthor,
                    0.1,
                    2000,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            // await AddRandomEntries(dbContext, dbChat, dbAdminAuthor, 1, 4, now, cancellationToken).ConfigureAwait(false);

            // We modify the DB directly here, so we have to invalidate everything
            await InvalidateEverything(cancellationToken).ConfigureAwait(false);

            Log.LogInformation("Default chat is created");
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to create default chat!");
            throw;
        }
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
        var blobs = Services.GetRequiredService<IBlobStorageProvider>();
        var audioBlobs = blobs.GetBlobStorage(BlobScope.AudioRecord);
        await audioBlobs.UploadByteStream(blobId, sourceBlobStream.Replay(cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        static FilePath GetAudioDataDir()
        {
            return new FilePath(Path.GetDirectoryName(typeof(ChatDbInitializer).Assembly.Location)) & "data";
        }
    }

    private async Task InvalidateEverything(CancellationToken cancellationToken)
    {
        // Signing in to admin session
        var commander = Services.Commander();
        var session = Services.GetRequiredService<ISessionFactory>().CreateSession();
        var accountsBackend = Services.GetRequiredService<IAccountsBackend>();
        var admin = await accountsBackend.Get(Constants.User.Admin.UserId, cancellationToken).ConfigureAwait(false);
        admin = admin.Require(AccountFull.MustBeAdmin);

        var signInCommand = new SignInCommand(session, admin.User, admin.User.Identities.Keys.Single());
        await commander.Call(signInCommand, cancellationToken).ConfigureAwait(false);

        var invalidateEverythingCommand = new IAccounts.InvalidateEverythingCommand(session, true);
        await Services.Commander().Run(invalidateEverythingCommand, cancellationToken).ConfigureAwait(false);
    }

    // Helpers

    private static readonly string[] RandomWords =
        { "most", "chat", "actual", "ever", "amazing", "absolutely", "terrific", "truly", "level 100500" };

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
