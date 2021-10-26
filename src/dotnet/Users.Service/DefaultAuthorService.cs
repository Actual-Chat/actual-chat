using ActualChat.Users.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Users;

public class DefaultAuthorService : DbServiceBase<UsersDbContext>, IDefaultAuthorService
{
    public DefaultAuthorService(IServiceProvider services) : base(services) { }

    public virtual async Task<IAuthorInfo> Get(UserId userId, CancellationToken cancellationToken)
    {
        DefaultAuthor? result = null;
        if (userId != UserId.None) {
            using var db = CreateDbContext(readWrite: false);
            var user = await db.Users
                .Include(u => u.DefaultAuthor)
                .FirstOrDefaultAsync(u => u.Id == (string)userId, cancellationToken)
                .ConfigureAwait(false);

            result = user?.DefaultAuthor?.ToModel();
        }
        if (result == null) {
            var nickname = GenerateRandomNickname();
            result = new() {
                Name = "NONAME",
                IsAnonymous = true,
                Nickname = nickname,
                Picture = "//eu.ui-avatars.com/api/?background=random&bold=true&length=1&name=" + nickname,
            };
        }
        return result;
    }

    private static readonly string[] _nicknamePartOne = new[]{
        "Professor",
        "Doctor",
        "Dinosaur",
        "Milkman",
        "Plumber",
    };

    // just some random elf names
    private static readonly string[] _nicknamePartTwo = new[]{
        "Zinsalor",
        "Ermys",
        "Vulmar",
        "Glynmenor",
        "Elauthin",
        "Helenelis",
        "Vaeril",
        "Bithana",
        "Bialaer",
        "Keypetor",
        "Ygannea",
        "Virnala",
        "Phaerille",
        "Helehana",
    };

    private static string GenerateRandomNickname()
    {
        var partOne = _nicknamePartOne[Random.Shared.Next(0, _nicknamePartOne.Length)];
        var partTwo = _nicknamePartTwo[Random.Shared.Next(0, _nicknamePartTwo.Length)];
        return partOne + partTwo + Random.Shared.Next(0, 100).ToString(CultureInfo.InvariantCulture);
    }
}
