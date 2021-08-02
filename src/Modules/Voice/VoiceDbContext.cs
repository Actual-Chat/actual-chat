using Microsoft.EntityFrameworkCore;

namespace ActualChat.Voice
{
    public class VoiceDbContext : DbContext
    {
        public VoiceDbContext(DbContextOptions options) : base(options) { }

        public DbSet<DbVoiceRecord> VoiceRecords { get; protected set; } = null!;
    }
}