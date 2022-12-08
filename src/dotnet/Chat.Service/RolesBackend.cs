using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat;

public class RolesBackend : DbServiceBase<ChatDbContext>, IRolesBackend
{
    private IChatsBackend? _chatsBackend;

    private IChatsBackend ChatsBackend => _chatsBackend ??= Services.GetRequiredService<IChatsBackend>();
    private IDbEntityResolver<string, DbRole> DbRoleResolver { get; }
    private IDbShardLocalIdGenerator<DbRole, string> DbRoleIdGenerator { get; }
    private DiffEngine DiffEngine { get; }

    public RolesBackend(IServiceProvider services) : base(services)
    {
        DbRoleResolver = services.GetRequiredService<IDbEntityResolver<string, DbRole>>();
        DbRoleIdGenerator = services.GetRequiredService<IDbShardLocalIdGenerator<DbRole, string>>();
        DiffEngine = services.GetRequiredService<DiffEngine>();
    }

    // [ComputeMethod]
    public virtual async Task<Role?> Get(ChatId chatId, RoleId roleId, CancellationToken cancellationToken)
    {
        if (roleId.ChatId != chatId)
            return null;

        var dbRole = await DbRoleResolver.Get(default, roleId, cancellationToken).ConfigureAwait(false);
        return dbRole?.ToModel();
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Role>> List(
        ChatId chatId, AuthorId authorId,
        bool isGuest, bool isAnonymous,
        CancellationToken cancellationToken)
    {
        // No need to call PseudoList - it's called by ListSystem anyway

        var systemRoles = await ListSystem(chatId, cancellationToken).ConfigureAwait(false);
        systemRoles = systemRoles.Where(IsInSystemRole).ToImmutableArray();

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbRoles = await dbContext.Roles
            .Where(r =>
                r.ChatId == chatId
                && (r.SystemRole == SystemRole.None || r.SystemRole == SystemRole.Owner)
                && dbContext.AuthorRoles.Any(ar => ar.DbAuthorId == authorId && ar.DbRoleId == r.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var roles = dbRoles
            .Select(r => r.ToModel())
            .Concat(systemRoles.Where(IsInSystemRole))
            .DistinctBy(r => r.Id)
            .OrderBy(r => r.Id.Id)
            .ToImmutableArray();
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
    public virtual async Task<ImmutableArray<Role>> ListSystem(
        ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return ImmutableArray<Role>.Empty;

        await PseudoList(chatId).ConfigureAwait(false);

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbRoles = await dbContext.Roles
            .Where(r => r.ChatId == chatId.Value && r.SystemRole != SystemRole.None)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var roles = dbRoles.Select(r => r.ToModel()).ToImmutableArray();
        return roles;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<AuthorId>> ListAuthorIds(
        ChatId chatId, RoleId roleId, CancellationToken cancellationToken)
    {
        var chat = await ChatsBackend.Get(chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null)
            return ImmutableArray<AuthorId>.Empty;

        await PseudoList(chatId).ConfigureAwait(false);

        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var dbAuthorIds = await dbContext.AuthorRoles
            .Where(ar => ar.DbRoleId == roleId.Value)
            .Select(ar => ar.DbAuthorId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var authorIds = dbAuthorIds.Select(id => new AuthorId(id)).ToImmutableArray();
        return authorIds;
    }

    // [CommandHandler]
    public virtual async Task<Role> Change(IRolesBackend.ChangeCommand command, CancellationToken cancellationToken)
    {
        var (chatId, roleId, expectedVersion, change) = command;
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            var invRole = context.Operation().Items.Get<Role>();
            if (invRole != null) {
                _ = Get(chatId, invRole.Id, default);
                _ = PseudoList(chatId);
            }
            return default!;
        }

        change.RequireValid();
        chatId = chatId.Require("Command.ChatId");
        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        // Fetching chat: if it doesn't exist, this command can't proceed anyway
        await dbContext.Chats.Get(chatId, cancellationToken).Require().ConfigureAwait(false);

        Role? role;
        DbRole? dbRole;
        if (change.IsCreate(out var update)) {
            roleId.RequireNone();
            var localId = await DbRoleIdGenerator
                .Next(dbContext, chatId, cancellationToken)
                .ConfigureAwait(false);
            roleId = new RoleId(chatId, localId, AssumeValid.Option);
            role = new Role(roleId) {
                Version = VersionGenerator.NextVersion(),
            };
            role = DiffEngine.Patch(role, update).Fix();
            dbRole = new DbRole(role);
            if (role.SystemRole != SystemRole.None) {
                var dbSameSystemRole = await dbContext.Roles.ForUpdate()
                    .SingleOrDefaultAsync(r => r.ChatId == dbRole.ChatId && r.SystemRole == dbRole.SystemRole, cancellationToken)
                    .ConfigureAwait(false);
                if (dbSameSystemRole != null)
                    throw StandardError.Constraint("Only one system role of a given kind is allowed.");
            }
            dbContext.Add(dbRole);
        }
        else {
            roleId.Require("Command.RoleId");
            dbRole = await dbContext.Roles.ForUpdate()
                .SingleOrDefaultAsync(r => r.ChatId == chatId && r.Id == roleId, cancellationToken)
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
                if (role.SystemRole is SystemRole.Owner or SystemRole.Anyone)
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
        if (!update.AuthorIds.IsEmpty() && !change.IsRemove()) {
            if (role.SystemRole is not SystemRole.None and not SystemRole.Owner)
                throw StandardError.Constraint("This system role uses automatic membership rules.");

            // Adding items
            foreach (var authorId in update.AuthorIds.AddedItems.Distinct())
                dbContext.AuthorRoles.Add(new() {
                    DbRoleId = roleId,
                    DbAuthorId = authorId
                });
            // Removing items
            var removedAuthorIds = update.AuthorIds.RemovedItems.Distinct().Select(i => i.Value).ToList();
            if (removedAuthorIds.Any()) {
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
        context.Operation().Items.Set(role);
        return role;
    }

    // Protected methods

    protected virtual Task<Unit> PseudoList(ChatId _)
        => Stl.Async.TaskExt.UnitTask;
}
