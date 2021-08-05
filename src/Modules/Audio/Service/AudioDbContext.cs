using Microsoft.EntityFrameworkCore;

namespace ActualChat.Audio
{
    public class AudioDbContext : DbContext
    {
        public DbSet<DbAudioRecord> AudioRecords { get; protected set; } = null!;

        public AudioDbContext(DbContextOptions options) : base(options) { }
    }
}
