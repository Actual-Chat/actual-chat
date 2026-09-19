using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

internal static class ChatImportGuard
{
    public static Task Lock(ChatDbContext db, ChatId chatId, CancellationToken cancellationToken)
        => db.ChatImports.Lock(chatId.ToMaintenanceKeyChain()[^1].Value, cancellationToken);

    public static async Task RequireAvailable(ChatDbContext db, ChatId chatId, CancellationToken cancellationToken)
    {
        await Lock(db, chatId, cancellationToken).ConfigureAwait(false);
        var ids = chatId.ToMaintenanceKeyChain().Select(x => ContentRef.Parse(x.Value).ContentId.Value).ToArray();
        if (await db.ChatImports.AnyAsync(x => x.IsActive && ids.Contains(x.Id), cancellationToken)
            .ConfigureAwait(false))
            throw StandardError.Constraint("The chat is in import maintenance mode.");
    }
}
