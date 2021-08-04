using System;
using ActualChat.Db;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Stl.DependencyInjection;

namespace ActualChat.Voice
{
    public class AudioDbContext : DbContext
    {
        public DbSet<DbAudioRecord> AudioRecords { get; protected set; } = null!;

        public AudioDbContext(DbContextOptions options) : base(options) { }
    }

    [RegisterService(typeof(IDataInitializer), IsEnumerable = true)]
    public class VoiceDbInitializer : DbInitializer<AudioDbContext>
    {
        public VoiceDbInitializer(IServiceProvider services) : base(services) { }
    }
}
