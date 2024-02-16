using ActualChat.Db;
using ActualChat.MLSearch.Db;

namespace ActualChat.MLSearch.Module;

public class MLSearchDbInitializer(IServiceProvider services) : DbInitializer<MLSearchDbContext>(services);
