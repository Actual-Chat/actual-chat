using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Audio.Db
{
    public class AudioDbContext : DbContext
    {
        public DbSet<DbAudioRecord> AudioRecords { get; protected set; } = null!;
        public DbSet<DbAudioSegment> AudioSegments { get; protected set; } = null!;
        // Stl.Fusion.EntityFramework tables
        public DbSet<DbOperation> Operations { get; protected set; } = null!;

        public AudioDbContext(DbContextOptions options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var audioRecording = modelBuilder.Entity<DbAudioRecord>();
            audioRecording
                .Property(ar => ar.AudioCodec)
                .HasConversion<string>();
            audioRecording
                .HasMany(e => e.Segments)
                .WithOne(s => s.AudioRecord)
                .HasForeignKey(s => s.RecordId);

            var audioSegment = modelBuilder.Entity<DbAudioSegment>();
            audioSegment.HasKey(e => new { RecordingId = e.RecordId, e.Index });
        }
    }
}
