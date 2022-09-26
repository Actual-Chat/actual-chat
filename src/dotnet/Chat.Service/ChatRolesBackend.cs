using System.ComponentModel.DataAnnotations;
using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Internal;
using Stl.Versioning;

namespace ActualChat.Chat;

public class ChatRolesBackend : DbServiceBase<ChatDbContext>, IChatRolesBackend
{
    private IChatsBackend? _chatsBackend;
    private IChatAuthorsBackend? _chatAuthorsBackend;

    private IAccountsBackend AccountsBackend { get; }
    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private IChatAuthorsBackend ChatAuthorsBackend => _chatAuthorsBackend ??= Services.GetRequiredService<IChatAuthorsBackend>();
    private IDbEntityResolver<string, DbChatRole> DbChatRoleResolver { get; }
    private IDbShardLocalIdGenerator<DbChatRole, string> DbChatRoleIdGenerator { get; }
    private DiffEngine DiffEngine { get; }

    public ChatRolesBackend(IServiceProvider services) : base(services)
    {
        AccountsBackend = Services.GetRequiredService<IAccountsBackend>();
        DbChatRoleResolver = Services.GetRequiredService<IDbEntityResolver<string, DbChatRole>>();
        DbChatRoleIdGenerator = Services.GetRequiredService<IDbShardLocalIdGenerator<DbChatRole, string>>();
        DiffEngine = Services.GetRequiredService<DiffEngine>();
    }

    // [ComputeMethod]
    public virtual async Task<ChatRole?> Get(string chatId, string roleId, CancellationToken cancellationToken)
    {
        var parsedChatRoleId = (ParsedChatRoleId)roleId;
        if (!(parsedChatRoleId.IsValid && parsedChatRoleId.ChatId == chatId))
            return null;

        var dbRole = await DbChatRoleResolver.Get(default, roleId, cancellationToken).ConfigureAwait(false);
        return dbRole?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ChatRole>> List(string chatId, string authorId,
        bool isAuthenticated, bool isAnonymous,
        CancellationToken cancellationToken)
    {
        // No need to call PseudoList - it's called by ListSystem anyway

        var systemRoles = await ListSystem(chatId, cancellationToken).ConfigureAwait(false);
        systemRoles = systemRoles.Where(IsInSystemRole).ToImmutableArray();

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbRoles = await dbContext.ChatRoles
            .Where(r =>
                r.ChatId == chatId
                && (r.SystemRole == SystemChatRole.None || r.SystemRole == SystemChatRole.Owner)
                && dbContext.ChatAuthorRoles.Any(ar => ar.DbChatAuthorId == authorId && ar.DbChatRoleId == r.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var roles = dbRoles
            .Select(r => r.ToModel())
            .Concat(systemRoles.Where(IsInSystemRole))
            .DistinctBy(r => r.Id)
            .OrderBy(r => r.Id)
            .ToImmutableArray();
        return roles;

        bool IsInSystemRole(ChatRole role)
            => role.SystemRole switch {
                SystemChatRole.Anyone => true,
                SystemChatRole.Unauthenticated => !isAuthenticated,
                SystemChatRole.Regular => isAuthenticated && !isAnonymous,
                SystemChatRole.Anonymous => isAuthenticated && isAnonymous,
                _ => false,
            };
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<ChatRole>> ListSystem(
        string chatId, CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return ImmutableArray<ChatRole>.Empty;

        await PseudoList(chatId).ConfigureAwait(false);

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbRoles = await dbContext.ChatRoles
            .Where(r => r.ChatId == chatId && r.SystemRole != SystemChatRole.None)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var roles = dbRoles.Select(r => r.ToModel()).ToImmutableArray();
        return roles;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListAuthorIds(
        string chatId, string roleId, CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return ImmutableArray<Symbol>.Empty;

        await PseudoList(chatId).ConfigureAwait(false);

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbAuthorIds = await dbContext.ChatAuthorRoles
            .Where(ar => ar.DbChatRoleId == roleId)
            .Select(ar => ar.DbChatAuthorId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var authorIds = dbAuthorIds.Select(id => (Symbol)id).ToImmutableArray();
        return authorIds;
    }

    // [CommandHandler]
    public virtual async Task<ChatRole?> Change(IChatRolesBackend.ChangeCommand command, CancellationToken cancellationToken)
    {
        var context = CommandContext.GetCurrent();

        var (chatId, roleId, expectedVersion, change) = command;
        if (Computed.IsInvalidating()) {
            var invChatRole = context.Operation().Items.Get<ChatRole>();
            if (invChatRole != null) {
                _ = Get(chatId, invChatRole.Id, default);
                _ = PseudoList(chatId);
            }
            return default;
        }

        chatId = chatId.RequireNonEmpty("Command.ChatId");
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // Fetching chat: if it doesn't exist, this command can't proceed anyway
        await dbContext.Chats.FindAsync(DbKey.Compose(chatId)).Require().ConfigureAwait(false);

        ChatRole? chatRole;
        DbChatRole? dbChatRole;
        if (change.RequireValid().IsCreate(out var update)) {
            roleId.RequireEmpty("Command.RoleId");
            var localId = await DbChatRoleIdGenerator
                .Next(dbContext, chatId, cancellationToken)
                .ConfigureAwait(false);
            roleId = new ParsedChatRoleId(chatId, localId).Id;
            chatRole = new ChatRole(roleId) {
                Version = VersionGenerator.NextVersion(),
            };
            chatRole = DiffEngine.Patch(chatRole, update).Fix();
            dbChatRole = new DbChatRole(chatRole);
            if (chatRole.SystemRole != SystemChatRole.None) {
                var dbSameSystemRole = await dbContext.ChatRoles
                    .SingleOrDefaultAsync(r => r.ChatId == dbChatRole.ChatId && r.SystemRole == dbChatRole.SystemRole, cancellationToken)
                    .ConfigureAwait(false);
                if (dbSameSystemRole != null)
                    throw StandardError.Constraint("Only one system role of a given kind is allowed.");
            }
            dbContext.Add(dbChatRole);
        }
        else {
            roleId.RequireNonEmpty("Command.RoleId");
            dbChatRole = await dbContext.ChatRoles.ForUpdate()
                .SingleAsync(r => r.ChatId == chatId && r.Id == roleId, cancellationToken)
                .ConfigureAwait(false);
            chatRole = dbChatRole.ToModel();
            VersionChecker.RequireExpected(chatRole.Version, expectedVersion);

            if (change.IsUpdate(out update)) {
                if ((update.SystemRole ?? chatRole.SystemRole) != chatRole.SystemRole)
                    throw StandardError.Constraint("System role cannot be changed.");
                chatRole = chatRole with {
                    Version = VersionGenerator.NextVersion(chatRole.Version),
                };
                chatRole = DiffEngine.Patch(chatRole, update).Fix();
                dbChatRole.UpdateFrom(chatRole);
                dbContext.Update(dbChatRole);
            }
            else {
                // Remove
                if (chatRole.SystemRole is SystemChatRole.Owner or SystemChatRole.Anyone)
                    throw StandardError.Constraint("This system role cannot be removed.");

                var dbChatAuthorRoles = await dbContext.ChatAuthorRoles.ForUpdate()
                    .Where(ar => ar.DbChatRoleId == roleId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                dbContext.RemoveRange(dbChatAuthorRoles);
                dbContext.Remove(dbChatRole!);
            }
        }

        // Processing update.AuthorIds
        if (!update.AuthorIds.IsEmpty() && !change.IsRemove()) {
            if (chatRole.SystemRole is not SystemChatRole.None and not SystemChatRole.Owner)
                throw StandardError.Constraint("This system role uses automatic membership rules.");

            // Adding items
            foreach (var authorId in update.AuthorIds.AddedItems.Distinct())
                dbContext.ChatAuthorRoles.Add(new() {
                    DbChatRoleId = roleId,
                    DbChatAuthorId = authorId
                });
            // Removing items
            var removedAuthorIds = update.AuthorIds.RemovedItems.Distinct().Select(i => i.Value).ToList();
            if (removedAuthorIds.Any()) {
 #pragma warning disable MA0002
                var dbChatAuthorRoles = await dbContext.ChatAuthorRoles
                    .Where(ar => ar.DbChatRoleId == roleId && removedAuthorIds.Contains(ar.DbChatAuthorId))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (chatRole!.SystemRole == SystemChatRole.Owner) {
                    var remainingOwnerCount = await dbContext.ChatAuthors
                        .Where(a => a.ChatId == chatId && a.UserId != null && !a.HasLeft
                            && !removedAuthorIds.Contains(a.Id))
                        .CountAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (remainingOwnerCount == 0)
                        throw StandardError.Constraint("There must be at least one user in Owners role.");
                }
                dbContext.RemoveRange(dbChatAuthorRoles);
 #pragma warning restore MA0002
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        chatRole = dbChatRole?.ToModel();
        context.Operation().Items.Set(chatRole);
        return chatRole;
    }

    // Protected methods

    protected virtual Task<Unit> PseudoList(string chatId)
        => Stl.Async.TaskExt.UnitTask;
}
