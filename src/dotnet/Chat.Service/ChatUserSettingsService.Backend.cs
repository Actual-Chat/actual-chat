using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public partial class ChatUserSettingsService
{
    // [ComputeMethod]
    public virtual async Task<ChatUserSettings?> Get(string userId, string chatId, CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty() || chatId.IsNullOrEmpty())
            return null;

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);
        // TODO(AY): Upgrade this code to use entity resolver
        var dbSettings = await dbContext.ChatUserSettings
            .SingleOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        return dbSettings?.ToModel();
    }

    // [CommandHandler]
    public virtual async Task<ChatUserSettings> Upsert(
        IChatUserSettingsBackend.UpsertCommand command,
        CancellationToken cancellationToken)
    {
        var (userId, chatId, settings) = command;
        if (Computed.IsInvalidating()) {
            _ = Get(userId, chatId, default);
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var id = DbChatUserSettings.ComposeId(chatId, userId);
        var originalVersion = settings.Version;
        settings = settings with { Version = VersionGenerator.NextVersion(originalVersion) };

        DbChatUserSettings dbSettings;
        if (originalVersion != 0) {
            dbSettings = await dbContext.ChatUserSettings
                .SingleAsync(e => e.Id == id && e.Version == originalVersion, cancellationToken)
                .ConfigureAwait(false);
            dbSettings.UpdateFrom(settings);
            dbContext.ChatUserSettings.Update(dbSettings);
        } else {
            dbSettings = new DbChatUserSettings() {
                Id = id,
                ChatId = chatId,
                UserId = userId,
            };
            dbSettings.UpdateFrom(settings);
            dbContext.ChatUserSettings.Add(dbSettings);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbSettings.ToModel();
    }
}
