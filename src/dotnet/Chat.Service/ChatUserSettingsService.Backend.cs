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
        var dbSettings = await dbContext.ChatUserConfigurations
            .SingleOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        var settings = dbSettings?.ToModel();
        return settings;
    }

    // Not a [ComputeMethod]!
    public virtual async Task<ChatUserSettings> GetOrCreate(string userId, string chatId, CancellationToken cancellationToken)
    {
        var result = await Get(userId, chatId, cancellationToken).ConfigureAwait(false);
        if (result != null)
            return result;

        var command = new IChatUserSettingsBackend.CreateCommand(chatId, userId);
        result = await _commander.Call(command, true, cancellationToken).ConfigureAwait(false);
        return result;
    }

    // [CommandHandler]
    public virtual async Task<ChatUserSettings> Create(
        IChatUserSettingsBackend.CreateCommand command, CancellationToken cancellationToken)
    {
        var (chatId, userId) = command;
        if (Computed.IsInvalidating()) {
            _ = Get(userId, chatId, default);
            return default!;
        }

        var dbSettings = new DbChatUserSettings {
            Id = DbChatUserSettings.ComposeId(chatId, userId),
            Version = VersionGenerator.NextVersion(),
            ChatId = chatId,
            UserId = userId,
        };

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);
        await dbContext.AddAsync(dbSettings, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbSettings.ToModel();
    }

    public virtual async Task SetLanguage(IChatUserSettingsBackend.SetLanguageCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            _ = Get(command.UserId, command.ChatId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbSettings = await dbContext.ChatUserConfigurations
            .ForUpdate()
            .SingleAsync(s => s.ChatId == command.ChatId && s.UserId == command.UserId, cancellationToken)
            .ConfigureAwait(false);
        dbSettings.Language = command.Language;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
