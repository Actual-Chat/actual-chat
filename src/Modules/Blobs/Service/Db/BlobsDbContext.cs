using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework.Extensions;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Blobs.Db
{
    public class BlobsDbContext : DbContext
    {
        // Stl.Fusion.EntityFramework tables
        public DbSet<DbKeyValue> KeyValues { get; protected set; } = null!;
        public DbSet<DbOperation> Operations { get; protected set; } = null!;

        public BlobsDbContext(DbContextOptions options) : base(options) { }
    }
}
