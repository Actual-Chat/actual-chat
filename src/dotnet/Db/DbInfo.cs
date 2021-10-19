using Microsoft.EntityFrameworkCore;

namespace ActualChat.Db;

public class DbInfo<TDbContext>
    where TDbContext : DbContext
{
    public DbKind DbKind { get; init; }
    public string ConnectionString { get; init; } = "";
}
