using System;
using ActualChat.Db;
using ActualChat.Hosting;
using Microsoft.EntityFrameworkCore;
using Stl.DependencyInjection;

namespace ActualChat.Audio
{
    public class AudioDbContext : DbContext
    {
        public DbSet<DbAudioRecord> AudioRecords { get; protected set; } = null!;

        public AudioDbContext(DbContextOptions options) : base(options) { }
    }

    [RegisterService(typeof(IDataInitializer), IsEnumerable = true)]
    public class AudioDbInitializer : DbInitializer<AudioDbContext>
    {
        public AudioDbInitializer(IServiceProvider services) : base(services) { }
    }
}
