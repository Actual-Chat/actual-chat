using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Module;

public class ChatDbInitializer : DbInitializer<ChatDbContext>
{
    public ChatDbInitializer(IServiceProvider services) : base(services)
    { }

    public override async Task InitializeData(CancellationToken cancellationToken)
    {
        // This initializer runs after everything else
        var chatDbInitializer = DbInitializer.GetCurrent<ChatDbInitializer>();
        await chatDbInitializer.WaitForOtherInitializers(_ => true).ConfigureAwait(false);

        await EnsureAnnouncementsChatExists(cancellationToken).ConfigureAwait(false);
        if (HostInfo.IsDevelopmentInstance)
            await EnsureDefaultChatExists(cancellationToken).ConfigureAwait(false);
    }

    public override async Task RepairData(CancellationToken cancellationToken)
    {
        // This initializer runs after everything else
        var chatDbInitializer = DbInitializer.GetCurrent<ChatDbInitializer>();
        await chatDbInitializer.WaitForOtherInitializers(_ => true).ConfigureAwait(false);

        await FixCorruptedReadPositions(cancellationToken).ConfigureAwait(false);
    }

    public override async Task VerifyData(CancellationToken cancellationToken)
    {
        if (!DbInfo.ShouldVerifyDb)
            return;

        var dbContext = DbHub.CreateDbContext(true);
        await using var _ = dbContext.ConfigureAwait(false);

        var chatIds = await dbContext.Chats
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var chatId in chatIds) {
            var thisChatEntries = dbContext.ChatEntries.Where(e => e.ChatId == chatId);
            var duplicateEntries = await (
                from e in thisChatEntries
                let count = thisChatEntries.Count(e1 => e1.LocalId == e.LocalId && e1.Kind == e.Kind)
                where count > 1
                select e
                ).ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (duplicateEntries.Count <= 0)
                continue;

            Log.LogCritical("Duplicate entries in Chat #{ChatId}:", chatId);
            foreach (var e in duplicateEntries)
                Log.LogCritical(
                    "- Entry w/ CompositeId = {CompositeId}, Id = {Id}, Type = {Type}, '{Content}'",
                    e.Id,
                    e.LocalId,
                    e.Kind,
                    e.Content);
        }
    }

    // Private methods

    private async Task EnsureAnnouncementsChatExists(CancellationToken cancellationToken)
    {
        var chatsBackend = Services.GetRequiredService<IChatsBackend>();
        var chatId = Constants.Chat.AnnouncementsChatId;
        var chat = await chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat != null)
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

    private async Task EnsureDefaultChatExists(CancellationToken cancellationToken)
    {
        var chatsBackend = Services.GetRequiredService<IChatsBackend>();
        var chatId = Constants.Chat.DefaultChatId;
        var chat = await chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat != null)
            return;

        try {
            Log.LogInformation("There is no default chat, creating one");
            var command = new IChatsUpgradeBackend.CreateDefaultChatCommand();
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Default chat is created");
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to create default chat!");
            throw;
        }
    }

    private async Task FixCorruptedReadPositions(CancellationToken cancellationToken)
    {
        try {
            Log.LogInformation("Fixing corrupted chat read positions");
            var command = new IChatsUpgradeBackend.FixCorruptedReadPositionsCommand();
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Done");
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to fix corrupted chat read positions");
            throw;
        }
    }
}
