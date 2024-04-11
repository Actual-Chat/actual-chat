using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Module;

public class ChatDbInitializer(IServiceProvider services) : DbInitializer<ChatDbContext>(services)
{
    public sealed record Options
    {
        public static readonly Options None = new();
        public static readonly Options Default = new() {
            AddAnnouncementsChat = true,
            AddDefaultChat = true,
        };

        public bool AddAnnouncementsChat { get; init; }
        // The options below are used on dev/test instances only
        public bool AddDefaultChat { get; init; }
        public bool AddNotesChat { get; init; }
        public bool AddFeedbackTemplateChat { get; init; }
    }

    public override async Task InitializeData(CancellationToken cancellationToken)
    {
        var options = Services.GetService<Options>() ?? Options.Default;

        // This initializer runs after everything else
        var chatDbInitializer = DbInitializer.GetCurrent<ChatDbInitializer>();
        await chatDbInitializer.WaitForOtherInitializers(_ => true).ConfigureAwait(false);

        if (options.AddAnnouncementsChat)
            await EnsureAnnouncementsChatExists(cancellationToken).ConfigureAwait(false);
        if (HostInfo.IsDevelopmentInstance) {
            if (options.AddDefaultChat)
                await EnsureDefaultChatExists(cancellationToken).ConfigureAwait(false);
            if (options.AddNotesChat)
                await EnsureNotesChatsExist(cancellationToken).ConfigureAwait(false);
            if (options.AddFeedbackTemplateChat)
                await EnsureFeedbackTemplateChatExists(cancellationToken).ConfigureAwait(false);
        }
    }

    public override async Task RepairData(CancellationToken cancellationToken)
    {
        // This initializer runs after everything else
        var chatDbInitializer = DbInitializer.GetCurrent<ChatDbInitializer>();
        await chatDbInitializer.WaitForOtherInitializers(_ => true).ConfigureAwait(false);

        // await FixCorruptedReadPositions(cancellationToken).ConfigureAwait(false);
    }

    public override Task VerifyData(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;

        // if (!DbInfo.ShouldVerifyDb)
        //     return;

        // There is no code there
        // previously we were verifying uniqueness of local Ids for chat entries
        // and now we have unique index `ix_chat_entries_chat_id_kind_local_id`
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
            var command = new ChatsUpgradeBackend_CreateAnnouncementsChat();
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
            var command = new ChatsUpgradeBackend_CreateDefaultChat();
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Default chat is created");
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to create default chat!");
            throw;
        }
    }

    private async Task EnsureFeedbackTemplateChatExists(CancellationToken cancellationToken)
    {
        var chatsBackend = Services.GetRequiredService<IChatsBackend>();
        var chatId = Constants.Chat.FeedbackTemplateChatId;
        var chat = await chatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat != null)
            return;

        try {
            Log.LogInformation("There is no 'Feedback' chat, creating one");
            var command = new ChatsUpgradeBackend_CreateFeedbackTemplateChat();
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("'Feedback' chat is created");
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to create 'Feedback' chat!");
            throw;
        }
    }

    private async Task EnsureNotesChatsExist(CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        // Get users who don't have Notes chat
        var userIds = await dbContext.Authors
            .Where(a => a.HasLeft == false && a.IsAnonymous == false)
            .Select(a => a.UserId)
            .Distinct()
            .Where(uid => !dbContext.Chats
                .Join(dbContext.Authors, c => c.Id, a => a.ChatId, (c, a) => new { c, a })
                .Any(x => x.a.UserId == uid && x.c.SystemTag == Constants.Chat.SystemTags.Notes.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (userIds.Count == 0)
            return;

        Log.LogInformation("There is no 'Notes' chat for some users, creating chats for them");

        foreach (var userId in userIds) {
            var createNotesChatCommand = new ChatsBackend_CreateNotesChat(UserId.Parse(userId));
            await Commander.Run(createNotesChatCommand, true, cancellationToken).ConfigureAwait(false);
        }

        Log.LogInformation("{Count} 'Notes' chats has been created", userIds.Count);
    }

    private async Task FixCorruptedReadPositions(CancellationToken cancellationToken)
    {
        try {
            Log.LogInformation("Fixing corrupted chat read positions");
            var command = new ChatsUpgradeBackend_FixCorruptedReadPositions();
            await Commander.Call(command, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Done");
        }
        catch (Exception e) {
            Log.LogCritical(e, "Failed to fix corrupted chat read positions");
            throw;
        }
    }
}
