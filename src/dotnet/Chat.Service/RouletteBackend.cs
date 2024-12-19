using ActualChat.Chat.Db;
using ActualChat.Roulette;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class RouletteBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IRouletteBackend
{
    [field: AllowNull, MaybeNull]
    private IDbEntityResolver<string, DbChatRoulette> DbChatRouletteResolver
        => field ??= Services.GetRequiredService<IDbEntityResolver<string, DbChatRoulette>>();

    public virtual async Task<ChatRouletteFull?> GetChatRoulette(
        ChatRouletteId id,
        CancellationToken cancellationToken)
    {
        if (id.IsNone)
            throw new ArgumentOutOfRangeException(nameof(id));

        var dbChatRoulette = await DbChatRouletteResolver.Get(id, cancellationToken).ConfigureAwait(false);
        return dbChatRoulette?.ToModel();
    }

    public virtual async Task<ChatRouletteFull> OnChangeChatRoulette(
        RouletteBackend_ChangeChatRoulette command,
        CancellationToken cancellationToken)
    {
        var (id, expectedVersion, change) = command;
        id.Require(nameof(RouletteBackend_ChangeChatRoulette.Id));
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            _ = GetChatRoulette(id, default);
            return default!;
        }


        change.RequireValid();
        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChatRoulette = await dbContext.ChatRoulettes.ForUpdate()
                // ReSharper disable once AccessToModifiedClosure
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                .ConfigureAwait(false);
        var oldChatRoulette = dbChatRoulette?.ToModel();
        if (change.IsCreate(out var chatRoulette)) {
            oldChatRoulette.RequireNull();
            chatRoulette = chatRoulette with {
                Id = id,
                Version = VersionGenerator.NextVersion(),
            };
            dbChatRoulette = new DbChatRoulette(chatRoulette);
            dbContext.ChatRoulettes.Add(dbChatRoulette);
            chatRoulette = dbChatRoulette.ToModel();
        }
        else if (change.IsUpdate(out chatRoulette)) {
            throw StandardError.Constraint(typeof(Change<ChatRoulette>), "Update chat is not allowed.");
        }
        else {
            // Remove change.
            dbChatRoulette.Require();
            dbChatRoulette.RequireVersion(expectedVersion);
            dbContext.Remove(dbChatRoulette);
            chatRoulette = dbChatRoulette.ToModel();
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return chatRoulette;
    }
}
