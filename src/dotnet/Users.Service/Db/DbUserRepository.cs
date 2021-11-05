using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Db;

public class DbUserRepository : DbUserRepo<UsersDbContext, DbUser, string>
{
    private readonly ClaimMapper _mapper;

    public DbUserRepository(
        DbAuthService<UsersDbContext>.Options options,
        IServiceProvider services,
        ClaimMapper mapper)
        : base(options, services)
        => _mapper = mapper;

    public override async Task<DbUser> Create(UsersDbContext dbContext, User user, CancellationToken cancellationToken = default)
    {
        var dbUser = await base.Create(dbContext, user, cancellationToken).ConfigureAwait(false);
        var dbUserAuthor = new DbUserAuthor();
        await _mapper
            .Apply(dbContext, dbUser, dbUserAuthor, dbUser.Claims, cancellationToken)
            .ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dbUser;
    }
}
