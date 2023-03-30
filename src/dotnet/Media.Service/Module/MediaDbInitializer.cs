using ActualChat.Db;
using ActualChat.Media.Db;

namespace ActualChat.Media.Module;

public partial class MediaDbInitializer : DbInitializer<MediaDbContext>
{
    public MediaDbInitializer(IServiceProvider services) : base(services)
    { }
}
