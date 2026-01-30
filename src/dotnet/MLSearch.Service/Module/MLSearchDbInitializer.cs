using ActualChat.Db;
using ActualChat.MLSearch.Db;

namespace ActualChat.MLSearch.Module;

public sealed class MLSearchDbInitializer(IServiceProvider services) : DbInitializer<MLSearchDbContext>(services);
