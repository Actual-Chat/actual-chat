using ActualChat.Chat.Db;
using ActualChat.Chat.Module;
using ActualChat.Mathematics.Internal;
using ActualLab.IO;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class ChatsUpgradeBackend
{
    // [CommandHandler]
    public virtual async Task<Chat> OnCreateAnnouncementsChat(
        ChatsUpgradeBackend_CreateAnnouncementsChat command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var chatId = Constants.Chat.AnnouncementsChatId;
        var hostInfo = Services.HostInfo();
        var userIds = await ListAllAccountIds(cancellationToken).ConfigureAwait(false);

        var admin = await AccountsBackend.Get(Constants.User.Admin.UserId, cancellationToken)
            .Require()
            .ConfigureAwait(false);
        var creatorId = (UserId?)admin.Id;

        var userIdByEmail = new Dictionary<string, UserId>(StringComparer.OrdinalIgnoreCase);
        foreach (var userId in userIds) {
            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (account == null)
                continue;

            if (account.Claims.Count == 0)
                continue;

            var emails = account.Identities.GetEmails();
            foreach (var email in emails) {
                if (hostInfo.IsDevelopmentInstance) {
                    if (email.EndsWith(Constants.Team.EmailSuffix, StringComparison.OrdinalIgnoreCase))
                        userIdByEmail.TryAdd(email, userId);
                }
                else if (string.Equals(email, Constants.Team.Member1Email, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(email, Constants.Team.Member2Email, StringComparison.OrdinalIgnoreCase))
                    userIdByEmail.TryAdd(email, userId);
            }
        }

        if (creatorId is null) {
            if (userIdByEmail.TryGetValue(Constants.Team.Member1Email, out var temp))
                creatorId = temp;
            else if (userIdByEmail.Count > 0)
                creatorId = userIdByEmail.First().Value;
        }
        if (creatorId is null)
            throw StandardError.Constraint("Creator user not found.");

        var changeCommand = new ChatsBackend_Change(chatId,
            null,
            new () {
                Create = new ChatDiff {
                    Title = Constants.Chat.AnnouncementsChatTitle,
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
        var ownerAuthorIds = Array.Empty<AuthorId>();
        foreach (var userId in userIdByEmail.Values) {
            if (userId == creatorId)
                continue;
            if (!authorByUserId.TryGetValue(userId, out var author))
                continue;

            ownerAuthorIds = ownerAuthorIds.With(author.Id);
        }

        if (ownerAuthorIds.Length > 0) {
            var changeOwnerRoleCmd = new RolesBackend_Change(chatId,
                ownerRole.Id,
                null,
                new () {
                    Update = new RoleDiff {
                        AuthorIds = new SetDiff<AuthorId[], AuthorId> {
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
        if (Invalidation.IsActive) {
            // This command changes a lot of things directly, so we invalidate everything here
            ComputedRegistry.InvalidateEverything();
            return null!;
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
        var userIds = await ListAllAccountIds(cancellationToken).ConfigureAwait(false);
        foreach (var userId in userIds) {
            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (!account.IsActive())
                continue;

            await AuthorsBackend.EnsureJoined(chatId, account.Id, cancellationToken).ConfigureAwait(false);
        }

        await AddAudioBlob("0000.webm", "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm").ConfigureAwait(false);
        await AddAudioBlob("0001.webm", "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm").ConfigureAwait(false);

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
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
                    .DbNextLocalId(dbContext, chatId, cancellationToken)
                    .ConfigureAwait(false);
                var id = ChatEntryId.New(chatId, localId);
                var entry = new DbChatEntry {
                    Id = id.Value,
                    ChatId = chatId.Value,
                    Kind = 0,
                    LocalId = localId,
                    Version = VersionGenerator.NextVersion(),
                    BeginsAt = lastBeginsAt,
                    EndsAt = lastEndsAt,
                    Content = $"{localId}: {content ?? GetRandomSentence(rnd, 30)}",
                    AuthorId = author.Id.Value,
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

                var timeMap = ConvertOldTextToTimeMap(
                    "{\"SourcePoints\":[0,4,18,20,25,27,37,46,53,57,64,74,81,93,98],\"TargetPoints\":[0,1.8,2.4,3.2,3.4,4.2,4.3,5.4,5.5,6.9,7.4,7.6,8.9,9.9,10.5]}");

                // Create a Media record for the audio blob
                var mediaId = MediaId.New(chatId.Value);
                var media = new MediaFull(mediaId) {
                    BlobId = "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm",
                    ContentType = "audio/webm",
                };
                var mediaChange = new MediaBackend_Change(mediaId, null, new Change<MediaFull> {
                    Create = media,
                });
                await Commander.Call(mediaChange, isOutermost: true, cancellationToken).ConfigureAwait(false);

                var localId = await ChatsBackend
                    .DbNextLocalId(dbContext, chatId, cancellationToken)
                    .ConfigureAwait(false);
                var id = ChatEntryId.New(chatId, localId);
                var textEntry = new DbChatEntry {
                    Id = id.Value,
                    ChatId = chatId.Value,
                    Kind = 0,
                    LocalId = localId,
                    Version = VersionGenerator.NextVersion(),
                    BeginsAt = lastBeginsAt,
                    EndsAt = lastEndsAt,
                    Content =
                        "Мой друг художник и поэт в Дождливый вечер на стекле мою любовь нарисовал "
                        + "открыв мне чудо на Земле",
                    TimeMap = timeMap,
                    AudioId = mediaId.Value,
                    AuthorId = author.Id.Value,
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

                // Create a Media record for the audio blob
                var mediaId = MediaId.New(chatId.Value);
                var media = new MediaFull(mediaId) {
                    BlobId = "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm",
                    ContentType = "audio/webm",
                };
                var mediaChange = new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Create = media });
                await Commander.Call(mediaChange, true, cancellationToken).ConfigureAwait(false);

                var localId = await ChatsBackend
                    .DbNextLocalId(dbContext, chatId, cancellationToken)
                    .ConfigureAwait(false);
                var id = ChatEntryId.New(chatId, localId);
                var textEntry = new DbChatEntry {
                    Id = id.Value,
                    ChatId = chatId.Value,
                    Kind = 0,
                    LocalId = localId,
                    Version = VersionGenerator.NextVersion(),
                    BeginsAt = lastBeginsAt,
                    EndsAt = lastEndsAt,
                    Content =
                        "утро в декабре туманом окутана под ногами белый снег предатель виден каждый "
                        + "шаг и холоду лютому слишком просто сладить с тобой",
                    TimeMap = timeMap,
                    AudioId = mediaId.Value,
                    AuthorId = author.Id.Value,
                };
                dbContext.Add(textEntry);
            }
        }

        async Task AddAudioBlob(FilePath fileName, string blobId)
        {
            var audioDataDir = new FilePath(typeof(ChatDbInitializer).Assembly.Location).DirectoryPath & "data";
            var filePath = audioDataDir & fileName;
            using var byteStream = filePath
                .ReadByteStream(1024, cancellationToken)
                .Memoize(CancellationToken.None);
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
            if (!textToTimeMapJson.StartsWith("{\"SourcePoints\"", StringComparison.OrdinalIgnoreCase))
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
        if (Invalidation.IsActive)
            return null!; // It just spawns other commands, so nothing to do here

        var chatId = Constants.Chat.FeedbackTemplateChatId;
        var hostInfo = Services.HostInfo();
        var userIds = await ListAllAccountIds(cancellationToken).ConfigureAwait(false);

        var admin = await AccountsBackend.Get(Constants.User.Admin.UserId, cancellationToken)
            .Require()
            .ConfigureAwait(false);
        var creatorId = (UserId?)null;

        var userIdByEmail = new Dictionary<string, UserId>(StringComparer.OrdinalIgnoreCase);
        foreach (var userId in userIds) {
            var account = await AccountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (account == null)
                continue;

            var emails = account.Identities.GetEmails();
            foreach (var email in emails) {
                if (hostInfo.IsDevelopmentInstance) {
                    if (email.EndsWith(Constants.Team.EmailSuffix, StringComparison.OrdinalIgnoreCase))
                        userIdByEmail.TryAdd(email, userId);
                }
                else if (string.Equals(email, Constants.Team.Member1Email, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(email, Constants.Team.Member2Email, StringComparison.OrdinalIgnoreCase))
                    userIdByEmail.TryAdd(email, userId);
            }
        }

        if (creatorId is null) {
            if (userIdByEmail.TryGetValue(Constants.Team.Member1Email, out var temp))
                creatorId = temp;
            else if (userIdByEmail.Count > 0)
                creatorId = userIdByEmail.First().Value;
        }
        creatorId ??= admin.Id ?? throw StandardError.Constraint("Creator user not found");

        var changeCommand = new ChatsBackend_Change(chatId,
            null,
            new () {
                Create = new ChatDiff {
                    Title = $"{CoreConstants.AppName} Feedback",
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
        var ownerAuthorIds = Array.Empty<AuthorId>();
        foreach (var userId in userIdByEmail.Values) {
            if (userId == creatorId)
                continue;
            if (!authorByUserId.TryGetValue(userId, out var author))
                continue;

            ownerAuthorIds = ownerAuthorIds.With(author.Id);
        }

        if (ownerAuthorIds.Length > 0) {
            var changeOwnerRoleCmd = new RolesBackend_Change(chatId,
                ownerRole.Id,
                null,
                new () {
                    Update = new RoleDiff {
                        AuthorIds = new SetDiff<AuthorId[], AuthorId> {
                            AddedItems = ownerAuthorIds,
                        },
                    },
                });
            await Commander.Call(changeOwnerRoleCmd, cancellationToken).ConfigureAwait(false);
        }

        return chat;
    }
}
