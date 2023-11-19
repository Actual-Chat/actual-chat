using ActualChat.Db;
using ActualChat.Media.Db;

namespace ActualChat.Media.Module;

public class MediaDbInitializer(IServiceProvider services) : DbInitializer<MediaDbContext>(services);
