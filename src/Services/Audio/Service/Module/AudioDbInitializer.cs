using System;
using ActualChat.Audio.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using Stl.DependencyInjection;

namespace ActualChat.Audio.Module
{
    [RegisterService(typeof(IDataInitializer), IsEnumerable = true)]
    public class AudioDbInitializer : DbInitializer<AudioDbContext>
    {
        public AudioDbInitializer(IServiceProvider services) : base(services) { }
    }
}
