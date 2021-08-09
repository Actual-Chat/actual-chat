using System;
using ActualChat.Audio.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using Stl.DependencyInjection;

namespace ActualChat.Audio.Module
{
    public class AudioDbInitializer : DbInitializer<AudioDbContext>
    {
        public AudioDbInitializer(IServiceProvider services) : base(services) { }
    }
}
