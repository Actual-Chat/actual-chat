using ActualChat.Chat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class AuthorsUpgradeBackend(IServiceProvider services)
    : DbServiceBase<ChatDbContext>(services), IAuthorsUpgradeBackend
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();

    public async Task<List<ChatId>> ListChatIds(UserId userId, CancellationToken cancellationToken)
    {
        if (userId.IsNone)
            return new List<ChatId>();

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var chatIds = await dbContext.Authors
            .Where(a => a.UserId == userId && !a.HasLeft)
            .Select(a => a.ChatId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return chatIds.Select(id => new ChatId(id)).ToList();
    }

    public async Task<List<ChatId>> ListOwnChatIds(Session session, CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        return await ListChatIds(account.Id, cancellationToken).ConfigureAwait(false);
    }
}
