using ActualChat.Chat.Db;
using ActualChat.Chat.Module;
using ActualChat.Hosting;
using ActualChat.Mathematics.Internal;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.IO;

namespace ActualChat.Chat;

public partial class ChatsUpgradeBackend
{
    // [CommandHandler]
    public virtual async Task<Chat> OnCreateAnnouncementsChat(
        ChatsUpgradeBackend_CreateAnnouncementsChat command,
        CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var chatId = Constants.Chat.AnnouncementsChatId;
        var usersTempBackend = Services.GetRequiredService<IUsersUpgradeBackend>();
        var hostInfo = Services.HostInfo();
        var userIds = await usersTempBackend.ListAllUserIds(cancellationToken).ConfigureAwait(false);

        var admin = await AccountsBackend.Get(Constants.User.Admin.UserId, cancellationToken)
            .Require()
            .ConfigureAwait(false);
        var creatorId = admin.Id;

        var userIdByEmail = new Dictionary<string, UserId>(StringComparer.OrdinalIgnoreCase);
        foreach (var userId in userIds) {
            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (account == null)
                continue;

            var user = account.User;
            if (user.Claims.Count == 0)
                continue;

            var email = account.GetVerifiedEmail();
            if (email.IsNullOrEmpty())
                continue;

            if (hostInfo.IsDevelopmentInstance) {
                if (email.OrdinalIgnoreCaseEndsWith(Constants.Team.EmailSuffix))
                    userIdByEmail.Add(email, userId);
            }
            else {
                if (OrdinalIgnoreCaseEquals(email, Constants.Team.Member1Email)
                    || OrdinalIgnoreCaseEquals(email, Constants.Team.Member2Email))
                    userIdByEmail.Add(email, userId);
            }
        }

        if (creatorId.IsNone) {
            if (userIdByEmail.TryGetValue(Constants.Team.Member1Email, out var temp))
                creatorId = temp;
            else if (userIdByEmail.Count > 0)
                creatorId = userIdByEmail.First().Value;
        }
        if (creatorId.IsNone)
            throw StandardError.Constraint("Creator user not found.");

        var changeCommand = new ChatsBackend_Change(chatId,
            null,
            new () {
                Create = new ChatDiff {
                    Title = "Actual Chat Announcements",
                    IsPublic = true,
                },
            },
            creatorId);
        var chat = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);

        var anyoneRole = await RolesBackend
            .GetSystem(chatId, SystemRole.Anyone, cancellationToken)
            .Require()
            .ConfigureAwait(false);

        var changeAnyoneRoleCmd = new RolesBackend_Change(chatId,
            anyoneRole.Id,
            null,
            new () {
                Update = new RoleDiff() {
                    Permissions = ChatPermissions.Read,
                },
            });
        await Commander.Call(changeAnyoneRoleCmd, cancellationToken).ConfigureAwait(false);

        // Join all existing users
        var authorByUserId = new Dictionary<UserId, AuthorFull>();
        foreach (var userId in userIds) {
            Log.LogInformation("Joining {UserId}", userId);
            var author = await AuthorsBackend.EnsureJoined(chatId, userId, cancellationToken).ConfigureAwait(false);
            authorByUserId.Add(userId, author);
        }

        var ownerRole = await RolesBackend
            .GetSystem(chatId, SystemRole.Owner, cancellationToken)
            .Require()
            .ConfigureAwait(false);
        var ownerAuthorIds = ApiArray<AuthorId>.Empty;
        foreach (var userId in userIdByEmail.Values) {
            if (userId == creatorId)
                continue;
            if (!authorByUserId.TryGetValue(userId, out var author))
                continue;

            ownerAuthorIds = ownerAuthorIds.Add(author.Id);
        }

        if (ownerAuthorIds.Count > 0) {
            var changeOwnerRoleCmd = new RolesBackend_Change(chatId,
                ownerRole.Id,
                null,
                new () {
                    Update = new RoleDiff {
                        AuthorIds = new SetDiff<ApiArray<AuthorId>, AuthorId> {
                            AddedItems = ownerAuthorIds,
                        },
                    },
                });
            await Commander.Call(changeOwnerRoleCmd, cancellationToken).ConfigureAwait(false);
        }

        return chat;
    }

    // [CommandHandler]
    public virtual async Task<Chat> OnCreateDefaultChat(ChatsUpgradeBackend_CreateDefaultChat command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            // This command changes a lot of things directly, so we invalidate everything here
            ComputedRegistry.Instance.InvalidateEverything();
            return default!;
        }

        var chatId = Constants.Chat.DefaultChatId;
        var randomWords = new[] { "most", "chat", "actual", "ever", "amazing", "absolutely", "terrific", "truly", "level 100500" };
        var audioBlobs = Services.GetRequiredService<IBlobStorages>()[BlobScope.AudioRecord];

        // Signing in admin
        var admin = await AccountsBackend.Get(Constants.User.Admin.UserId, cancellationToken).ConfigureAwait(false);
        admin.Require(AccountFull.MustBeAdmin);

        var changeCommand = new ChatsBackend_Change(chatId, null, new() {
            Create = new ChatDiff {
                Title = "The Actual One",
                IsPublic = true,
            },
        }, admin.Id);
        var chat = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
        var adminAuthor = await AuthorsBackend.EnsureJoined(chatId, admin.Id, cancellationToken).ConfigureAwait(false);

        // Adding every user to this chat
        var userIds = await UsersUpgradeBackend.ListAllUserIds(cancellationToken).ConfigureAwait(false);
        foreach (var userId in userIds) {
            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (!account.IsActive())
                continue;

            await AuthorsBackend.EnsureJoined(chatId, account.Id, cancellationToken).ConfigureAwait(false);
        }

        await AddAudioBlob("0000.webm", "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm").ConfigureAwait(false);
        await AddAudioBlob("0001.webm", "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm").ConfigureAwait(false);

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        await AddEntries(adminAuthor, 0.1, 2000, null).ConfigureAwait(false);
        // await AddEntries(adminAuthor, 1, 4, Clocks.SystemClock.Now).ConfigureAwait(false);

        return chat;

        async Task AddEntries(AuthorFull author, double audioProbability, int count, Moment? beginsAt)
        {
            var lastBeginsAt = beginsAt ?? Clocks.SystemClock.Now - TimeSpan.FromDays(1);
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
                else if (rnd.NextDouble() <= audioProbability) {
                    if (i % 2 == 0)
                        await AddAudioEntry1().ConfigureAwait(false);
                    else
                        await AddAudioEntry2().ConfigureAwait(false);
                }
                else
                    await AddTextEntry().ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;

            async Task AddTextEntry(string? content = null)
            {
                lastEndsAt += TimeSpan.FromSeconds(rnd.NextDouble() * 5);
                lastBeginsAt = lastEndsAt;
                var localId = await ChatsBackend
                    .DbNextLocalId(dbContext, chatId, ChatEntryKind.Text, cancellationToken)
                    .ConfigureAwait(false);
                var id = new ChatEntryId(chatId, ChatEntryKind.Text, localId, AssumeValid.Option);
                var entry = new DbChatEntry {
                    Id = id,
                    ChatId = chatId,
                    Kind = ChatEntryKind.Text,
                    LocalId = localId,
                    Version = VersionGenerator.NextVersion(),
                    BeginsAt = lastBeginsAt,
                    EndsAt = lastEndsAt,
                    Content = $"{localId}: {content ?? GetRandomSentence(rnd, 30)}",
                    AuthorId = author.Id,
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

                var localId = await ChatsBackend
                    .DbNextLocalId(dbContext, chatId, ChatEntryKind.Audio, cancellationToken)
                    .ConfigureAwait(false);
                var id = new ChatEntryId(chatId, ChatEntryKind.Audio, localId, AssumeValid.Option);
                var timeMap = ConvertOldTextToTimeMap(
                    "{\"SourcePoints\":[0,4,18,20,25,27,37,46,53,57,64,74,81,93,98],\"TargetPoints\":[0,1.8,2.4,3.2,3.4,4.2,4.3,5.4,5.5,6.9,7.4,7.6,8.9,9.9,10.5]}");
                var audioEntry = new DbChatEntry {
                    Id = id,
                    ChatId = chatId,
                    Kind = ChatEntryKind.Audio,
                    LocalId = localId,
                    Version = VersionGenerator.NextVersion(),
                    BeginsAt = lastBeginsAt,
                    EndsAt = lastEndsAt,
                    Content = "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm",
                    AuthorId = author.Id,
                };
                dbContext.Add(audioEntry);

                localId = await ChatsBackend
                    .DbNextLocalId(dbContext, chatId, ChatEntryKind.Text, cancellationToken)
                    .ConfigureAwait(false);
                id = new ChatEntryId(chatId, ChatEntryKind.Text, localId, AssumeValid.Option);
                var textEntry = new DbChatEntry {
                    Id = id,
                    ChatId = chatId,
                    Kind = ChatEntryKind.Text,
                    LocalId = localId,
                    Version = VersionGenerator.NextVersion(),
                    BeginsAt = lastBeginsAt,
                    EndsAt = lastEndsAt,
                    Content =
                        "Мой друг художник и поэт в Дождливый вечер на стекле мою любовь нарисовал "
                        + "открыв мне чудо на Земле",
                    TimeMap = timeMap,
                    AudioEntryId = audioEntry.LocalId,
                    AuthorId = author.Id,
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

                var timeMap = ConvertOldTextToTimeMap(
                    "{\"SourcePoints\":[0,5,31,35,53,63,69,76,82,119,121,126],\"TargetPoints\":[0,1.4,3,3.6,4.8,5.3,6,6.3,7,9.5,9.5,10.53]}");
                var localId = await ChatsBackend
                    .DbNextLocalId(dbContext, chatId, ChatEntryKind.Audio, cancellationToken)
                    .ConfigureAwait(false);
                var id = new ChatEntryId(chatId, ChatEntryKind.Audio, localId, AssumeValid.Option);
                var audioEntry = new DbChatEntry {
                    Id = id,
                    ChatId = chatId,
                    Kind = ChatEntryKind.Audio,
                    LocalId = localId,
                    Version = VersionGenerator.NextVersion(),
                    BeginsAt = lastBeginsAt,
                    EndsAt = lastEndsAt,
                    Content = "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm",
                    AuthorId = author.Id,
                };
                dbContext.Add(audioEntry);

                localId = await ChatsBackend
                    .DbNextLocalId(dbContext, chatId, ChatEntryKind.Text, cancellationToken)
                    .ConfigureAwait(false);
                id = new ChatEntryId(chatId, ChatEntryKind.Text, localId, AssumeValid.Option);
                var textEntry = new DbChatEntry {
                    Id = id,
                    ChatId = chatId,
                    Kind = ChatEntryKind.Text,
                    LocalId = localId,
                    Version = VersionGenerator.NextVersion(),
                    BeginsAt = lastBeginsAt,
                    EndsAt = lastEndsAt,
                    Content =
                        "утро в декабре туманом окутана под ногами белый снег предатель виден каждый "
                        + "шаг и холоду лютому слишком просто сладить с тобой",
                    TimeMap = timeMap,
                    AudioEntryId = audioEntry.LocalId,
                    AuthorId = author.Id,
                };
                dbContext.Add(textEntry);
            }
        }

        async Task AddAudioBlob(FilePath fileName, string blobId)
        {
            var audioDataDir = new FilePath(typeof(ChatDbInitializer).Assembly.Location).DirectoryPath & "data";
            var filePath = audioDataDir & fileName;
            var byteStream = filePath.ReadByteStream(1024, cancellationToken).Memoize(CancellationToken.None);
            await audioBlobs
                .UploadByteStream(blobId, byteStream.Replay(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }

        string GetRandomSentence(Random random, int maxLength)
            => Enumerable
                .Range(0, random.Next(maxLength))
                .Select(_ => randomWords[Random.Shared.Next(randomWords.Length)])
                .ToDelimitedString(" ")
                .Capitalize();

        string ConvertOldTextToTimeMap(string textToTimeMapJson)
        {
            if (!textToTimeMapJson.OrdinalIgnoreCaseStartsWith("{\"SourcePoints\""))
                return textToTimeMapJson;

            var oldMap = NewtonsoftJsonSerialized.New<OldLinearMap>(textToTimeMapJson).Value;
            var newMap = oldMap.ToLinearMap();
            textToTimeMapJson = NewtonsoftJsonSerialized.New(newMap).Data;
            return textToTimeMapJson;
        }
    }

    // [CommandHandler]
    public virtual async Task<Chat> OnCreateFeedbackTemplateChat(ChatsUpgradeBackend_CreateFeedbackTemplateChat command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var chatId = Constants.Chat.FeedbackTemplateChatId;
        var usersTempBackend = Services.GetRequiredService<IUsersUpgradeBackend>();
        var hostInfo = Services.HostInfo();
        var userIds = await usersTempBackend.ListAllUserIds(cancellationToken).ConfigureAwait(false);

        var admin = await AccountsBackend.Get(Constants.User.Admin.UserId, cancellationToken)
            .Require()
            .ConfigureAwait(false);
        var creatorId = UserId.None;

        var userIdByEmail = new Dictionary<string, UserId>(StringComparer.OrdinalIgnoreCase);
        foreach (var userId in userIds) {
            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (account == null)
                continue;

            var email = account.GetVerifiedEmail();
            if (email.IsNullOrEmpty())
                continue;

            if (hostInfo.IsDevelopmentInstance) {
                if (email.OrdinalIgnoreCaseEndsWith(Constants.Team.EmailSuffix))
                    userIdByEmail.Add(email, userId);
            }
            else {
                if (OrdinalIgnoreCaseEquals(email, Constants.Team.Member1Email)
                    || OrdinalIgnoreCaseEquals(email, Constants.Team.Member2Email))
                    userIdByEmail.Add(email, userId);
            }
        }

        if (creatorId.IsNone) {
            if (userIdByEmail.TryGetValue(Constants.Team.Member1Email, out var temp))
                creatorId = temp;
            else if (userIdByEmail.Count > 0)
                creatorId = userIdByEmail.First().Value;
        }
        if (creatorId.IsNone) {
            if (admin.Id.IsNone)
                throw StandardError.Constraint("Creator user not found");

            creatorId = admin.Id;
        }

        var changeCommand = new ChatsBackend_Change(chatId,
            null,
            new () {
                Create = new ChatDiff {
                    Title = "Actual Chat Feedback",
                    IsPublic = true,
                    IsTemplate = true,
                    AllowGuestAuthors = true,
                    Kind = ChatKind.Group,
                },
            },
            creatorId);
        var chat = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);

        var anyoneRole = await RolesBackend
            .GetSystem(chatId, SystemRole.Anyone, cancellationToken)
            .Require()
            .ConfigureAwait(false);

        var changeAnyoneRoleCmd = new RolesBackend_Change(chatId,
            anyoneRole.Id,
            null,
            new () {
                Update = new RoleDiff() {
                    Permissions = ChatPermissions.Read,
                },
            });
        await Commander.Call(changeAnyoneRoleCmd, cancellationToken).ConfigureAwait(false);

        // Join owners
        var authorByUserId = new Dictionary<UserId, AuthorFull>();
        foreach (var userId in userIdByEmail.Values) {
            Log.LogInformation("Joining {UserId}", userId);
            var author = await AuthorsBackend.EnsureJoined(chatId, userId, cancellationToken).ConfigureAwait(false);
            authorByUserId.Add(userId, author);
        }

        var ownerRole = await RolesBackend
            .GetSystem(chatId, SystemRole.Owner, cancellationToken)
            .Require()
            .ConfigureAwait(false);
        var ownerAuthorIds = ApiArray<AuthorId>.Empty;
        foreach (var userId in userIdByEmail.Values) {
            if (userId == creatorId)
                continue;
            if (!authorByUserId.TryGetValue(userId, out var author))
                continue;

            ownerAuthorIds = ownerAuthorIds.Add(author.Id);
        }

        if (ownerAuthorIds.Count > 0) {
            var changeOwnerRoleCmd = new RolesBackend_Change(chatId,
                ownerRole.Id,
                null,
                new () {
                    Update = new RoleDiff {
                        AuthorIds = new SetDiff<ApiArray<AuthorId>, AuthorId> {
                            AddedItems = ownerAuthorIds,
                        },
                    },
                });
            await Commander.Call(changeOwnerRoleCmd, cancellationToken).ConfigureAwait(false);
        }

        return chat;
    }

    // [CommandHandler]
    public virtual async Task<Chat> OnCreateAiChat(ChatsUpgradeBackend_CreateAiChat command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating())
            return default!; // It just spawns other commands, so nothing to do here

        var chatId = Constants.Chat.AiChatId;
        var historyMessages = new[] {
            "what design ideas we discussed yesterday",
            "send me summary of all of last week's discussions",
            "when sending the invoice for the work",
            "translate into English the main points from yesterday morning's call",
            "go to 24 May",
            "write a greeting for your parents",
            "Make the message X shorter",
            "show all posts tagged with #idea",
            "what design ideas we discussed yesterday",
            "show all messages from Alex",
            "write down the top 5 most talked about bugs from the last week.",
        };
        var searchSummaryMessages = new[] {
            "A thread about faith. Claudius thinks there's no God. Ophelia thinks there is a God. Hamlet doubts God, but does not deny it.",
        };
        var searchResultMessages = new[] {
            "Loudness is perceived as the \"loudness\" or of a sound and is relate loudness is perceived as the \"loudness\" or of a sound and is relate",
            "сame all the conversations in which we discussed the idea of a new search сame all the conversations in which we discussed the idea of a new search",
        };
        var promptMessages = new[] {
            "anything in the chat",
            "by date",
            "by contact",
            "in period",
            "help with the answer",
            "summarize all day conversations",
        };
        var randomWords = new[] { "most", "chat", "actual", "ever", "amazing", "absolutely", "terrific", "truly", "level 100500" };

        // Signing in admin
        var admin = await AccountsBackend.Get(Constants.User.Admin.UserId, cancellationToken).ConfigureAwait(false);
        admin.Require(AccountFull.MustBeAdmin);

        var changeCommand = new ChatsBackend_Change(chatId, null, new() {
            Create = new ChatDiff {
                Title = "AI Chat",
                IsPublic = true,
            },
        }, admin.Id);
        var chat = await Commander.Call(changeCommand, true, cancellationToken).ConfigureAwait(false);
        var adminAuthor = await AuthorsBackend.EnsureJoined(chatId, admin.Id, cancellationToken).ConfigureAwait(false);

        var dbContext = await DbHub.CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        await AddEntries(adminAuthor, historyMessages, null).ConfigureAwait(false);
        await AddEntries(adminAuthor, searchSummaryMessages, null, isSearchSummary: true, count: 2).ConfigureAwait(false);
        await AddEntries(adminAuthor, searchResultMessages, null, isSearchResult: true).ConfigureAwait(false);
        await AddEntries(adminAuthor, promptMessages, null).ConfigureAwait(false);

        return chat;

        async Task AddEntries(AuthorFull author,
            string[] messages,
            Moment? beginsAt,
            bool isSearchResult = false,
            bool isSearchSummary = false,
            int count = 0)
        {
            var lastBeginsAt = beginsAt ?? Clocks.SystemClock.Now - TimeSpan.FromDays(1);
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
            if (count == 0) {
                foreach (var message in messages)
                    await AddTextEntry(message).ConfigureAwait(false);
            }
            else {
                for (var i = 0; i < count; i++) {
                    foreach (var message in messages)
                        await AddTextEntry(message).ConfigureAwait(false);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;

            async Task AddTextEntry(string? content = null)
            {
                lastEndsAt += TimeSpan.FromSeconds(rnd.NextDouble() * 5);
                lastBeginsAt = lastEndsAt;
                var localId = await ChatsBackend
                    .DbNextLocalId(dbContext, chatId, ChatEntryKind.Text, cancellationToken)
                    .ConfigureAwait(false);
                var id = new ChatEntryId(chatId, ChatEntryKind.Text, localId, AssumeValid.Option);
                var entry = new DbChatEntry {
                    Id = id,
                    ChatId = chatId,
                    Kind = ChatEntryKind.Text,
                    LocalId = localId,
                    Version = VersionGenerator.NextVersion(),
                    BeginsAt = lastBeginsAt,
                    EndsAt = lastEndsAt,
                    Content = $"{content ?? GetRandomSentence(rnd, 30)}",
                    AuthorId = author.Id,
                    IsSearchResult = isSearchResult,
                    IsSearchSummary = isSearchSummary,
                };
                dbContext.Add(entry);
            }
        }

        string GetRandomSentence(Random random, int maxLength)
            => Enumerable
                .Range(0, random.Next(maxLength))
                .Select(_ => randomWords[Random.Shared.Next(randomWords.Length)])
                .ToDelimitedString(" ")
                .Capitalize();
    }
}
