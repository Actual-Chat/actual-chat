using ActualChat.Db;
using ActualChat.Search.Db;

namespace ActualChat.Search.Module;

public class SearchDbInitializer(IServiceProvider services) : DbInitializer<SearchDbContext>(services);
