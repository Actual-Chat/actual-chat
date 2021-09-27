using System;
using ActualChat.Audio.Db;
using ActualChat.Db;

namespace ActualChat.Audio.Module
{
    public class AudioDbInitializer : DbInitializer<AudioDbContext>
    {
        public AudioDbInitializer(IServiceProvider services) : base(services) { }
    }
}
