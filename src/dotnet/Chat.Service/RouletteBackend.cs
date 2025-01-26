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
    [field: AllowNull, MaybeNull]
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    [field: AllowNull, MaybeNull]
    private IAuthorsBackend AuthorsBackend => field ??= Services.GetRequiredService<IAuthorsBackend>();
    private DiffEngine DiffEngine { get; } = services.GetRequiredService<DiffEngine>();

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
        var hasCompleted = false;

        var dbChatRoulette = await dbContext.ChatRoulettes.ForUpdate()
                // ReSharper disable once AccessToModifiedClosure
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                .ConfigureAwait(false);
        var oldChatRoulette = dbChatRoulette?.ToModel();
        ChatRouletteFull chatRoulette;
        if (change.IsCreate(out var update)) {
            oldChatRoulette.RequireNull();
            if (update.CompletedBy.HasValue || update.CompleteReason.HasValue)
                throw StandardError.Constraint("The chat roulette being created should not be marked as completed.");

            chatRoulette = new ChatRouletteFull(id);
            chatRoulette = DiffEngine.Patch(chatRoulette, update) with {
                Version = VersionGenerator.NextVersion(),
            };
            if (chatRoulette.ChatId.IsNone)
                throw StandardError.Constraint("ChatId must be provided.");
            if (chatRoulette.UserId1.IsNone)
                throw StandardError.Constraint("UserId1 must be provided.");
            if (chatRoulette.UserId2.IsNone)
                throw StandardError.Constraint("UserId2 must be provided.");
            if (chatRoulette.InitiatedBy.IsNone)
                throw StandardError.Constraint("InitiatedBy must be provided.");
            if (chatRoulette.InitiatedBy != chatRoulette.UserId1 && chatRoulette.InitiatedBy != chatRoulette.UserId2)
                throw StandardError.Constraint("InitiatedBy must be equal to either UserId1 or UserId2.");

            dbChatRoulette = new DbChatRoulette(chatRoulette);
            dbContext.ChatRoulettes.Add(dbChatRoulette);
            chatRoulette = dbChatRoulette.ToModel();
        }
        else if (change.IsUpdate(out update)) {
            dbChatRoulette.Require();
            dbChatRoulette.RequireVersion(expectedVersion);
            oldChatRoulette.Require();

            if (update.ChatId.HasValue)
                throw StandardError.Constraint("ChatId can't be changed.");
            if (update.UserId1.HasValue)
                throw StandardError.Constraint("UserId1 can't be changed.");
            if (update.UserId2.HasValue)
                throw StandardError.Constraint("UserId2 can't be changed.");
            if (update.InitiatedBy.HasValue)
                throw StandardError.Constraint("InitiatedBy can't be changed.");

            if (update.CompletedBy.HasValue) {
                if (!oldChatRoulette.CompletedBy.IsNone)
                    throw StandardError.Constraint("Already completed.");
                if (update.CompletedBy.Value.IsNone)
                    throw StandardError.Constraint("CompletedBy must be filled in with not none value.");
                if (update.CompleteReason is null || update.CompleteReason == CompleteChatRouletteReason.None)
                    throw StandardError.Constraint("CompleteReason should be defined on completion.");

                hasCompleted = true;
            }
            else {
                if (update.CompleteReason.HasValue)
                    throw StandardError.Constraint("Complete reason should be specified only with CompletedBy.");
            }

            chatRoulette = DiffEngine.Patch(oldChatRoulette, update) with {
                Version = VersionGenerator.NextVersion(),
            };
            if (hasCompleted)
                chatRoulette = chatRoulette with {
                    CompletedAt = Clocks.SystemClock.Now
                };

            dbChatRoulette.UpdateFrom(chatRoulette);
        }
        else {
            // Remove change.
            dbChatRoulette.Require();
            dbChatRoulette.RequireVersion(expectedVersion);
            dbContext.Remove(dbChatRoulette);
            chatRoulette = dbChatRoulette.ToModel();
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (hasCompleted)
            // Raise events
            context.Operation.AddEvent(new ChatRouletteCompletedEvent(chatRoulette));

        return chatRoulette;
    }

    // Events
    // [EventHandler]
    public virtual async Task OnAuthorLeftEvent(AuthorUpsertedEvent eventCommand, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var (author, oldAuthor) = eventCommand;
        if (author.ChatId.Kind != ChatKind.Group)
            return;

        if (!author.HasLeft)
            return;

        var oldHasLeft = oldAuthor?.HasLeft ?? true;
        if (oldHasLeft == author.HasLeft)
            return;

        var chat = await ChatsBackend.Get(author.ChatId, cancellationToken).ConfigureAwait(false);
        if (chat is null || !chat.IsChatRoulette())
            return;

        var chatRouletteId = await RouletteExt.GetChatRouletteId(chat.Id, AuthorsBackend, cancellationToken).ConfigureAwait(false);
        if (chatRouletteId.IsNone)
            return;

        var chatRoulette = await GetChatRoulette(chatRouletteId, cancellationToken).ConfigureAwait(false);
        if (chatRoulette is null || !chatRoulette.CompletedBy.IsNone)
            return;

        var diff = new ChatRouletteFullDiff {
            CompletedBy = author.UserId,
            CompleteReason = CompleteChatRouletteReason.Leave
        };
        var chatRouletteCommand = new RouletteBackend_ChangeChatRoulette(chatRoulette.Id, chatRoulette.Version, Change.Update(diff));
        await Commander.Call(chatRouletteCommand, true, cancellationToken).ConfigureAwait(false);
    }
}
