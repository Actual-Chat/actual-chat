using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Audio.Db
{
    public class AudioDbContext : DbContext
    {
        public DbSet<DbAudioRecord> AudioRecords { get; protected set; } = null!;
        public DbSet<DbOperation> Operations { get; protected set; } = null!;

        public AudioDbContext(DbContextOptions options) : base(options) { }
    }
}
