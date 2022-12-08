using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Module;

public partial class ChatDbInitializer : DbInitializer<ChatDbContext>
{
    private async Task Upgrade(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        Log.LogInformation("Upgrading DB...");
        // Log.LogInformation("Upgrading DB: no upgrades");
        // return Task.CompletedTask;

        // These steps are already executed on prod:
        // await UpgradeChats(dbContext, cancellationToken).ConfigureAwait(false);
        // await UpgradePermissions(dbContext, cancellationToken).ConfigureAwait(false);
        // await EnsureAnnouncementsChatExists(dbContext, cancellationToken).ConfigureAwait(false);
        await FixCorruptedLastReadPositions(dbContext, cancellationToken).ConfigureAwait(false);
    }

    // Active upgrade steps

    // Archived upgrade steps

    private async Task UpgradeChats(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        var candidateChatIds = await dbContext.Chats
            .Where(c => c.Owners.Any())
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (candidateChatIds.Count == 0) {
            Log.LogInformation("No chats to upgrade");
            return;
        }

        try {
            Log.LogInformation("Upgrading {ChatCount} chats...", candidateChatIds.Count);
            foreach (var chatId in candidateChatIds) {
                var command = new IChatsUpgradeBackend.UpgradeChatCommand(new ChatId(chatId));
                await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            }
            Log.LogInformation("Chats are upgraded");
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to upgrade chats!");
            throw;
        }
    }

    private async Task UpgradePermissions(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        var chatId = Constants.Chat.AnnouncementsChatId.Value;
        var candidateDbRoles = await dbContext.Roles
            .Where(c => c.SystemRole == SystemRole.Anyone)
            .Where(c => c.ChatId != chatId)
            .Where(c => !c.CanLeave || !c.CanSeeMembers)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidateDbRoles.Count == 0) {
            Log.LogInformation("No roles to upgrade");
            return;
        }

        try {
            Log.LogInformation("Upgrading roles ({RolesNumber}) permissions...", candidateDbRoles.Count);

            foreach (var dbRole in candidateDbRoles) {
                dbRole.CanLeave = true;
                dbRole.CanSeeMembers = true;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            Log.LogInformation("Role permissions are upgraded");
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to upgrade role permissions!");
            throw;
        }
    }

    private async Task EnsureAnnouncementsChatExists(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        var chatId = Constants.Chat.AnnouncementsChatId.Value;
        var exists = await dbContext.Chats
            .Where(c => c.Id == chatId)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
        if (exists)
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

    private async Task FixCorruptedLastReadPositions(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        try {
            Log.LogInformation("FixCorruptedReadPositions: started");
            var command = new IChatsUpgradeBackend.FixCorruptedReadPositionsCommand();
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("FixCorruptedReadPositions: completed");
        }
        catch (Exception e) {
            Log.LogCritical(e, "FixCorruptedReadPositions failed");
            throw;
        }
    }
}
