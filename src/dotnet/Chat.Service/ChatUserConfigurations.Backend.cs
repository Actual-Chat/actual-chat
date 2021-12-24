using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public partial class ChatUserConfigurations
{
    // [ComputeMethod]
    public virtual async Task<ChatUserConfiguration?> Get(string userId, string chatId, CancellationToken cancellationToken)
    {
        if (userId.IsNullOrEmpty() || chatId.IsNullOrEmpty())
            return null;

        ChatUserConfiguration? chatAuthorOptions;
        var dbContext = CreateDbContext();
        await using (var _ = dbContext.ConfigureAwait(false)) {
            var dbChatAuthorOptions = await dbContext.ChatUserConfigurations
                .SingleOrDefaultAsync(a => a.ChatId == chatId && a.UserId == userId, cancellationToken)
                .ConfigureAwait(false);
            chatAuthorOptions = dbChatAuthorOptions?.ToModel();
        }

        return chatAuthorOptions;
    }

    // Not a [ComputeMethod]!
    public virtual async Task<ChatUserConfiguration> GetOrCreate(string userId, string chatId, CancellationToken cancellationToken)
    {
        var chatAuthorOptions = await Get(userId, chatId, cancellationToken).ConfigureAwait(false);
        if (chatAuthorOptions != null)
            return chatAuthorOptions;

        var createAuthorOptionsCommand = new IChatUserConfigurationsBackend.CreateCommand(chatId, userId);
        chatAuthorOptions = await _commander.Call(createAuthorOptionsCommand, true, cancellationToken).ConfigureAwait(false);

        return chatAuthorOptions;
    }

    // [CommandHandler]
    public virtual async Task<ChatUserConfiguration> Create(IChatUserConfigurationsBackend.CreateCommand command, CancellationToken cancellationToken)
    {
        var (chatId, userId) = command;
        if (Computed.IsInvalidating()) {
            _ = Get(userId, chatId, default);
            return default!;
        }

        var dbChatAuthorOptions = new DbChatUserConfiguration {
            ChatId = chatId,
            UserId = userId
        };

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var maxLocalId = await dbContext.ChatUserConfigurations.ForUpdate() // To serialize inserts
            .Where(e => e.ChatId == chatId)
            .OrderByDescending(e => e.LocalId)
            .Select(e => e.LocalId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        dbChatAuthorOptions.LocalId = await _idSequences.Next(chatId, maxLocalId).ConfigureAwait(false);
        dbChatAuthorOptions.Id = DbChatUserConfiguration.ComposeId(chatId, dbChatAuthorOptions.LocalId);

        await dbContext.AddAsync(dbChatAuthorOptions, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbChatAuthorOptions.ToModel();
    }

    public virtual async Task SetLanguage(IChatUserConfigurationsBackend.SetLanguageCommand command, CancellationToken cancellationToken)
    {
        if (Computed.IsInvalidating()) {
            _ = Get(command.UserId, command.ChatId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChatUserConfiguration = await dbContext.ChatUserConfigurations
            .ForUpdate()
            .SingleAsync(s => s.ChatId == command.ChatId && s.UserId == command.UserId, cancellationToken)
            .ConfigureAwait(false);
        dbChatUserConfiguration.Language = command.Language ?? "";
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
