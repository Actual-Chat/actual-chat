using ActualChat.Db;
using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class UserAvatarsBackend : DbServiceBase<UsersDbContext>, IUserAvatarsBackend
{
    private readonly ICommander _commander;
    private readonly IDbEntityResolver<string,DbUserAvatar> _dbUserAvatarResolver;
    private readonly IDbShardLocalIdGenerator<DbUserAvatar, string> _dbUserAvatarLocalIdGenerator;

    public UserAvatarsBackend(IServiceProvider services) : base(services)
    {
        _commander = Services.GetRequiredService<ICommander>();
        _dbUserAvatarResolver = Services.GetRequiredService<IDbEntityResolver<string, DbUserAvatar>>();
        _dbUserAvatarLocalIdGenerator = Services.GetRequiredService<IDbShardLocalIdGenerator<DbUserAvatar, string>>();
    }

    // [ComputeMethod]
    public virtual async Task<UserAvatar?> Get(string avatarId, CancellationToken cancellationToken)
    {
        if (avatarId.IsNullOrEmpty())
            return null;
        var dbUserAvatar = await _dbUserAvatarResolver.Get(avatarId, cancellationToken).ConfigureAwait(false);
        var userAvatar = dbUserAvatar?.ToModel();
        return userAvatar;
    }

    // [ComputeMethod]
    public virtual async Task<ImmutableArray<Symbol>> ListAvatarIds(string userId, CancellationToken cancellationToken)
    {
        var dbContext = CreateDbContext();
        await using var _ = dbContext.ConfigureAwait(false);

        var avatarIds = await dbContext.UserAvatars
            .Where(c => c.UserId==userId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return avatarIds.Select(x => new Symbol(x)).ToImmutableArray();
    }

    public Task<Symbol> GetAvatarIdByChatAuthorId(string chatAuthorId, CancellationToken cancellationToken)
    {
        var avatarId = new Symbol(DbUserAvatar.ComposeId(chatAuthorId, UserAvatarType.AnonymousChatAuthor, 1));
        return Task.FromResult(avatarId);
    }

    public async Task<UserAvatar> EnsureChatAuthorAvatarCreated(string chatAuthorId, string name, CancellationToken cancellationToken)
    {
        var avatarId = DbUserAvatar.ComposeId(chatAuthorId, UserAvatarType.AnonymousChatAuthor, 1);
        var avatar = await Get(avatarId, cancellationToken).ConfigureAwait(false);
        if (avatar != null) {
            if (name.IsNullOrEmpty() || OrdinalEquals(avatar.Name, name))
                return avatar;

            var updateCommand = new IUserAvatarsBackend.UpdateCommand(avatar.Id, name, avatar.Picture, avatar.Bio);
            await _commander.Call(updateCommand, true, cancellationToken).ConfigureAwait(false);
        }
        else {
            var createCommand = new IUserAvatarsBackend.CreateCommand(chatAuthorId, name);
            avatar = await _commander.Call(createCommand, true, cancellationToken).ConfigureAwait(false);
        }
        return avatar;
    }

    // [CommandHandler]
    public virtual async Task<UserAvatar> Create(IUserAvatarsBackend.CreateCommand command, CancellationToken cancellationToken)
    {
        var principalId = command.PrincipalId;
        var userId = !principalId.OrdinalContains(":") ? principalId : "";
        var context = CommandContext.GetCurrent();

        if (Computed.IsInvalidating()) {
            if (!userId.IsNullOrEmpty())
                _ = ListAvatarIds(userId, default);
            var invUserAvatar = context.Operation().Items.Get<UserAvatar>()!;
            _ = Get(invUserAvatar.Id, default);
            return default!;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        string id;
        long nextLocalId;
        if (!userId.IsNullOrEmpty()) {
            nextLocalId = await _dbUserAvatarLocalIdGenerator.Next(dbContext, userId, cancellationToken).ConfigureAwait(false);
            id = DbUserAvatar.ComposeId(userId, UserAvatarType.User, nextLocalId);
        }
        else {
            nextLocalId = 1;
            id = DbUserAvatar.ComposeId(principalId, UserAvatarType.AnonymousChatAuthor, nextLocalId);
        }

        var dbUserAvatar = new DbUserAvatar {
            Id = id,
            Version = VersionGenerator.NextVersion(),
            UserId = userId,
            LocalId = nextLocalId,
            Name = command.Name,
            Bio = "",
            Picture = ""
        };
        dbContext.UserAvatars.Add(dbUserAvatar);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var userAvatar = dbUserAvatar.ToModel();
        context.Operation().Items.Set(userAvatar);
        return userAvatar;
    }

    // [CommandHandler]
    public virtual async Task Update(IUserAvatarsBackend.UpdateCommand command, CancellationToken cancellationToken)
    {
        var avatarId = command.AvatarId;

        if (Computed.IsInvalidating()) {
            _ = Get(avatarId, default);
            return;
        }

        var dbContext = await CreateCommandDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbUserAvatar = await dbContext.UserAvatars
            .SingleAsync(a => a.Id == avatarId, cancellationToken)
            .ConfigureAwait(false);

        dbUserAvatar.Name = command.Name;
        dbUserAvatar.Picture = command.Picture;
        dbUserAvatar.Bio = command.Bio;
        dbUserAvatar.Version = VersionGenerator.NextVersion();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
