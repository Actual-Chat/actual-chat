using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Db;

public class DbUserRepository : DbUserRepo<UsersDbContext, DbUser, string>
{
    private readonly IClaimsToAuthorMapper _mapper;

    public DbUserRepository(
        DbAuthService<UsersDbContext>.Options options,
        IServiceProvider services,
        IClaimsToAuthorMapper mapper)
        : base(options, services)
        => _mapper = mapper;

    public override async Task<DbUser> Create(UsersDbContext dbContext, User user, CancellationToken cancellationToken = default)
    {
        var dbUser = await base.Create(dbContext, user, cancellationToken).ConfigureAwait(false);
        dbUser.DefaultAuthor = new();
        await _mapper.Populate(dbUser.DefaultAuthor, dbUser.Claims).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbUser;
    }
}
