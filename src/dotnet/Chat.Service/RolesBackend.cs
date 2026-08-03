using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

/// <summary>
/// Backend service implementation for managing chat roles and permissions.
/// </summary>
public class RolesBackend(IServiceProvider services) : DbServiceBase<ChatDbContext>(services), IRolesBackend
{
    private IChatsBackend ChatsBackend => field ??= Services.GetRequiredService<IChatsBackend>();
    private IDbEntityResolver<string, DbRole> DbRoleResolver { get; }
        = services.GetRequiredService<IDbEntityResolver<string, DbRole>>();
    private IDbShardLocalIdGenerator<DbRole, string> DbRoleIdGenerator { get; }
        = services.GetRequiredService<IDbShardLocalIdGenerator<DbRole, string>>();
    private DiffEngine DiffEngine { get; } = services.GetRequiredService<DiffEngine>();

    // [ComputeMethod]
    public virtual async Task<Role?> Get(ChatId chatId, RoleId roleId, CancellationToken cancellationToken)
    {
        if (roleId.ChatId != chatId)
            return null;

        var dbRole = await DbRoleResolver.Get(roleId.Value, cancellationToken).ConfigureAwait(false);
        return dbRole?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<Role[]> List(
        ChatId chatId, AuthorId authorId,
        bool isGuest, bool isAnonymous,
        CancellationToken cancellationToken)
    {
        // No need to call PseudoList - it's called by ListSystem anyway

        var systemRoles = await ListSystem(chatId, cancellationToken).ConfigureAwait(false);
        systemRoles = systemRoles.Where(IsInSystemRole).ToArray();

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbRoles = await dbContext.Roles
            .Where(r =>
                r.ChatId == chatId.Value
                && (r.SystemRole == SystemRole.None
                    || r.SystemRole == SystemRole.Owner
                    || r.SystemRole == SystemRole.Moderator)
                && dbContext.AuthorRoles.Any(ar => ar.DbAuthorId == authorId.Value && ar.DbRoleId == r.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var roles = dbRoles
            .Select(r => r.ToModel())
            .Concat(systemRoles.Where(IsInSystemRole))
            .DistinctBy(r => r.Id)
            .OrderBy(r => r.Id.Id)
            .ToArray();
        return roles;

        bool IsInSystemRole(Role role)
            => role.SystemRole switch {
                SystemRole.Anyone => true,
                SystemRole.Guest => isGuest,
                SystemRole.User => !isGuest && !isAnonymous,
                SystemRole.AnonymousUser => !isGuest && isAnonymous,
                _ => false,
            };
    }

    // [ComputeMethod]
    public virtual async Task<Role[]> ListSystem(
        ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return [];

        await PseudoList(chatId).ConfigureAwait(false);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbRoles = await dbContext.Roles
            .Where(r => r.ChatId == chatId.Value && r.SystemRole != SystemRole.None)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var roles = dbRoles.Select(r => r.ToModel()).ToArray();
        return roles;
    }

    // [ComputeMethod]
    public virtual async Task<AuthorId[]> ListAuthorIds(
        ChatId chatId, RoleId roleId, CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return [];

        await PseudoList(chatId).ConfigureAwait(false);

        var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        var dbAuthorIds = await dbContext.AuthorRoles
            .Where(ar => ar.DbRoleId == roleId.Value)
            .Select(ar => ar.DbAuthorId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var authorIds = dbAuthorIds.Select(AuthorId.Parse).ToArray();
        return authorIds;
    }

    // [CommandHandler]
    public virtual async Task<Role> Change(RolesBackend_Change command, CancellationToken cancellationToken)
    {
        var (chatId, roleId, expectedVersion, change) = command;
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invRole = context.Operation.Items.KeylessGet<Role>();
            if (invRole != null) {
                _ = Get(chatId, invRole.Id, default);
                _ = PseudoList(chatId);
            }
            return default!;
        }

        change.RequireValid();
        chatId.Require("Command.ChatId");
        if (chatId.IsThread() || chatId is PeerChatId)
            throw StandardError.Constraint("Roles are not supported in thread or peer chats.");

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // Fetching chat: if it doesn't exist, this command can't proceed anyway
        var dbChat = await dbContext.Chats.Get(chatId.Value, cancellationToken).Require().ConfigureAwait(false);

        Role? role;
        DbRole? dbRole;
        if (change.IsCreate(out var update)) {
            roleId.RequireNull();
            var localId = await DbRoleIdGenerator
                .Next(dbContext, chatId.Value, cancellationToken)
                .ConfigureAwait(false);
            roleId = RoleId.New(chatId, localId);
            role = new Role(roleId) {
                Version = VersionGenerator.NextVersion(),
            };
            role = DiffEngine.Patch(role, update).Fix();
            dbRole = new DbRole(role);
            if (role.SystemRole != SystemRole.None) {
                var dbSameSystemRole = await dbContext.Roles.ForUpdate()
                    .FirstOrDefaultAsync(r => r.ChatId == dbRole.ChatId && r.SystemRole == dbRole.SystemRole, cancellationToken)
                    .ConfigureAwait(false);
                if (dbSameSystemRole != null)
                    throw StandardError.Constraint("Only one system role of a given kind is allowed.");
            }
            dbContext.Add(dbRole);
        }
        else {
            roleId.Require("Command.RoleId");
            dbRole = await dbContext.Roles.ForUpdate()
                .FirstOrDefaultAsync(r => r.ChatId == chatId.Value && r.Id == roleId.Value, cancellationToken)
                .ConfigureAwait(false);
            dbRole = dbRole.RequireVersion(expectedVersion);
            role = dbRole.ToModel();

            if (change.IsUpdate(out update)) {
                if ((update.SystemRole ?? role.SystemRole) != role.SystemRole)
                    throw StandardError.Constraint("System role cannot be changed.");
                role = role with {
                    Version = VersionGenerator.NextVersion(role.Version),
                };
                role = DiffEngine.Patch(role, update).Fix();
                dbRole.UpdateFrom(role);
            }
            else {
                // Remove
                if (role.SystemRole is SystemRole.Owner or SystemRole.Anyone or SystemRole.Moderator)
                    throw StandardError.Constraint("This system role cannot be removed.");

                var dbAuthorRoles = await dbContext.AuthorRoles.ForUpdate()
                    .Where(ar => ar.DbRoleId == roleId.Value)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                dbContext.RemoveRange(dbAuthorRoles);
                dbContext.Remove(dbRole!);
            }
        }

        // Processing update.AuthorIds
        if (!update.AuthorIds.IsEmpty && !change.IsRemove()) {
            if (role.SystemRole is not SystemRole.None and not SystemRole.Owner and not SystemRole.Moderator)
                throw StandardError.Constraint("This system role uses automatic membership rules.");

            var existingAuthorIds = (await dbContext.AuthorRoles
                .Where(ar => ar.DbRoleId == roleId.Value)
                .Select(ar => ar.DbAuthorId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)
                ).Select(AuthorId.Parse)
                .ToHashSet();

            // Adding items
            var addedAuthorIds = update.AuthorIds.AddedItems
                .Where(x => !existingAuthorIds.Contains(x))
                .Distinct()
                .ToList();
            var chat = dbChat.ToModel();
            if (chat.HasSingleAuthor) {
                var totalAuthors = existingAuthorIds.Count + addedAuthorIds.Count;
                if (totalAuthors > 1)
                    throw StandardError.Constraint("There can be only one author in this chat.");
            }

            foreach (var authorId in addedAuthorIds) {
                if (authorId.ChatId != roleId.ChatId)
                    throw StandardError.Constraint("Author and role must belong to the same chat.");

                dbContext.AuthorRoles.Add(new() {
                    DbRoleId = roleId.Value,
                    DbAuthorId = authorId.Value,
                });
            }
            // Removing items
            var removedAuthorIds = update.AuthorIds.RemovedItems
                .Distinct()
                .Select(i => i.Value)
                .ToList();
            if (removedAuthorIds.Count != 0) {
#pragma warning disable MA0002
                var dbAuthorRoles = await dbContext.AuthorRoles
                    .Where(ar => ar.DbRoleId == roleId.Value && removedAuthorIds.Contains(ar.DbAuthorId))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (role!.SystemRole == SystemRole.Owner) {
                    var remainingOwnerCount = await dbContext.Authors
                        .Where(a => a.ChatId == chatId.Value && a.UserId != null && !a.HasLeft
                            && !removedAuthorIds.Contains(a.Id))
                        .CountAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (remainingOwnerCount == 0)
                        throw StandardError.Constraint("There must be at least one user in Owners role.");
                }
                dbContext.RemoveRange(dbAuthorRoles);
#pragma warning restore MA0002
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        role = dbRole.ToModel();
        context.Operation.Items.KeylessSet(role);
        return role;
    }

    // Protected methods

    [ComputeMethod]
    protected virtual Task<Unit> PseudoList(ChatId _)
        => ActualLab.Async.TaskExt.UnitTask;
}
