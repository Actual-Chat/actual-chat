using Microsoft.EntityFrameworkCore;

namespace ActualChat.Db.Module;

public class DbInfo<TDbContext>
    where TDbContext : DbContext
{
    public DbKind DbKind { get; init; }
    public string ConnectionString { get; init; } = "";
    public bool ShouldRecreateDb { get; set; }
    public bool ShouldMigrateDb { get; set; }
    public bool ShouldRepairDb { get; set; }
    public bool ShouldVerifyDb { get; set; }
}
