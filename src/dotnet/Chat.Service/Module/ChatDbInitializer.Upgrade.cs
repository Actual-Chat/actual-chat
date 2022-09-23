using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Module;

public partial class ChatDbInitializer : DbInitializer<ChatDbContext>
{
    private Task Upgrade(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        // Log.LogInformation("Upgrading DB...");
        Log.LogInformation("Upgrading DB: no upgrades");
        return Task.CompletedTask;

        // These steps are already executed on prod:
        // await UpgradeChats(dbContext, cancellationToken).ConfigureAwait(false);
        // await UpgradeChatRolesPermissions(dbContext, cancellationToken).ConfigureAwait(false);
        // await EnsureAnnouncementsChatExists(dbContext, cancellationToken).ConfigureAwait(false);
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
                var cmd = new IChatsBackend.UpgradeChatCommand(chatId);
                await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
            }
            Log.LogInformation("Chats are upgraded");
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to upgrade chats!");
            throw;
        }
    }

    private async Task UpgradeChatRolesPermissions(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        var chatId = (string)Constants.Chat.AnnouncementsChatId;
        var candidateRoles = await dbContext.ChatRoles
            .Where(c => c.SystemRole == SystemChatRole.Anyone)
            .Where(c => c.ChatId != chatId)
            .Where(c => !c.CanLeave || !c.CanSeeMembers)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidateRoles.Count == 0) {
            Log.LogInformation("No chat roles to upgrade");
            return;
        }

        try {
            Log.LogInformation("Upgrading chat roles ({RolesNumber}) permissions...", candidateRoles.Count);

            foreach (var chatRole in candidateRoles) {
                chatRole.CanLeave = true;
                chatRole.CanSeeMembers = true;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            Log.LogInformation("Chat roles permissions are upgraded");
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to upgrade chat roles permissions!");
            throw;
        }
    }

    private async Task EnsureAnnouncementsChatExists(ChatDbContext dbContext, CancellationToken cancellationToken)
    {
        var chatId = (string)Constants.Chat.AnnouncementsChatId;
        var exists = await dbContext.Chats
            .Where(c => c.Id == chatId)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
        if (exists)
            return;

        try {
            Log.LogInformation("There is no 'Announcements' chat, creating one");
            var cmd = new IChatsBackend.CreateAnnouncementsChatCommand();
            await Commander.Call(cmd, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("'Announcements' chat is created");
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to create 'Announcements' chat!");
            throw;
        }
    }
}
