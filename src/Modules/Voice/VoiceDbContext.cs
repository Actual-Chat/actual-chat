using System;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.DependencyInjection;

namespace ActualChat.Voice
{
    public class VoiceDbContext : DbContext
    {
        public DbSet<DbAudioRecord> AudioRecords { get; protected set; } = null!;

        public VoiceDbContext(DbContextOptions options) : base(options) { }
    }

    [RegisterService(typeof(IDbInitializer), IsEnumerable = true)]
    public class VoiceDbInitializer : DbInitializer<VoiceDbContext>
    {
        public VoiceDbInitializer(IServiceProvider services) : base(services) { }
    }
}
