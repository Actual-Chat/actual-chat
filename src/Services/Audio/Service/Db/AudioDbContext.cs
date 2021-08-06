using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Audio.Db
{
    public class AudioDbContext : DbContext
    {
        public DbSet<DbAudioRecording> AudioRecordings { get; protected set; } = null!;
        public DbSet<DbAudioSegment> AudioSegments { get; protected set; } = null!;
        
        public DbSet<DbOperation> Operations { get; protected set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var audioRecording = modelBuilder.Entity<DbAudioRecording>();
            audioRecording
                .Property(ar => ar.AudioCodec)
                .HasConversion<string>();
            audioRecording
                .HasMany(e => e.Segments)
                .WithOne();

            var audioSegment = modelBuilder.Entity<DbAudioSegment>();
            audioSegment.HasKey(e => new { e.RecordingId, e.Index });
        }

        public AudioDbContext(DbContextOptions options) : base(options) { }
    }
}
