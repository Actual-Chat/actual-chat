using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserMigratorBackend(IServiceProvider services): DbServiceBase<UsersDbContext>(services), IUserMigratorBackend
{
    public virtual async Task<bool> OnMoveChatToPlace(
        UserMigratorBackend_MoveChatToPlace command,
        CancellationToken cancellationToken)
    {
        var (chatId, placeId) = command;
        var placeChatId = new PlaceChatId(PlaceChatId.Format(placeId, chatId.Id));
        var newChatId = (ChatId)placeChatId;

        if (Computed.IsInvalidating()) {
            // Skip invalidation. Value for old chat should no longer be requested.
            return default;
        }

        var chatSid = chatId.Value;
        var hasChanges = false;

        Log.LogInformation("About to move chat '{ChatId}' to place '{PlaceId}'", chatSid, placeId);

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        hasChanges |= await UpdateUserChatSettings(dbContext, chatId, newChatId, cancellationToken).ConfigureAwait(false);

        hasChanges |= await UpdateChatPositions(dbContext, chatId, newChatId, cancellationToken).ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return hasChanges;
    }

    private async Task<bool> UpdateUserChatSettings(
        UsersDbContext dbContext,
        ChatId oldChatId,
        ChatId newChatId,
        CancellationToken cancellationToken)
    {
        var oldChatSettingsSuffix = UserChatSettings.GetKvasKey(oldChatId);
        var newChatSettingsSuffix = UserChatSettings.GetKvasKey(newChatId);

        // Update UserChatSettings
#pragma warning disable CA1307
        var updateCount = await dbContext.KvasEntries
            .Where(c => c.Key.EndsWith(oldChatSettingsSuffix))
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Key, c => c.Key.Replace(oldChatSettingsSuffix, newChatSettingsSuffix)),
                cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore CA1307

        Log.LogInformation("Updated {Count} UserChatSettings kvas records", updateCount);
        return updateCount > 0;
    }

    private async Task<bool> UpdateChatPositions(
        UsersDbContext dbContext,
        ChatId oldChatId,
        ChatId newChatId,
        CancellationToken cancellationToken)
    {
        var oldChatPositionSuffix = $" {oldChatId.Value}:0";
        var newChatPositionSuffix = $" {newChatId.Value}:0";

        // Update UserChatSettings
#pragma warning disable CA1307
        var updateCount = await dbContext.ChatPositions
            .Where(c => c.Id.EndsWith(oldChatPositionSuffix))
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Id, c => c.Id.Replace(oldChatPositionSuffix, newChatPositionSuffix)),
                cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore CA1307

        Log.LogInformation("Updated {Count} ChatPositions records", updateCount);
        return updateCount > 0;
    }
}
