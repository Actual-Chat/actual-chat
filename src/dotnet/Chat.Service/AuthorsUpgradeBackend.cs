using ActualChat.Chat.Db;
using ActualChat.Kvas;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class AuthorsUpgradeBackend : DbServiceBase<ChatDbContext>, IAuthorsUpgradeBackend
{
    private IAccounts Accounts { get; }
    private IServerKvas ServerKvas { get; }
    private IAuthorsBackend Backend { get; }

    public AuthorsUpgradeBackend(IServiceProvider services) : base(services)
    {
        Accounts = services.GetRequiredService<IAccounts>();
        ServerKvas = services.GetRequiredService<IServerKvas>();
        Backend = services.GetRequiredService<IAuthorsBackend>();
    }

    public async Task<List<ChatId>> ListChatIds(UserId userId, CancellationToken cancellationToken)
    {
        if (userId.IsEmpty)
            return new List<ChatId>();

        var dbContext = CreateDbContext();
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
