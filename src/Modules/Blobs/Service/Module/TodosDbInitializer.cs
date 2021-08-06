using System;
using ActualChat.Blobs.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using Stl.DependencyInjection;

namespace ActualChat.Blobs.Module
{
    [RegisterService(typeof(IDataInitializer), IsEnumerable = true)]
    public class BlobsDbInitializer : DbInitializer<BlobsDbContext>
    {
        public BlobsDbInitializer(IServiceProvider services) : base(services) { }
    }
}
