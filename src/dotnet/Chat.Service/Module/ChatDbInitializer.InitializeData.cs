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

        Log.LogInformation("Waiting for other initializers to complete...");
        await Task.WhenAll(dependencies).ConfigureAwait(false);

        Log.LogInformation("Initializing data...");
        await EnsureAnnouncementsChatExists(cancellationToken).ConfigureAwait(false);
        if (HostInfo.IsDevelopmentInstance)
            await EnsureDefaultChatExists(cancellationToken).ConfigureAwait(false);
    }

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
}
