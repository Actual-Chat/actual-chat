using ActualChat.Db;

namespace ActualChat.Flows.Db;

public class FlowsDbInitializer(IServiceProvider services) : DbInitializer<FlowsDbContext>(services);
