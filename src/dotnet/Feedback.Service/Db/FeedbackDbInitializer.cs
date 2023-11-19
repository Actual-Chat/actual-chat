using ActualChat.Db;

namespace ActualChat.Feedback.Db;

public class FeedbackDbInitializer(IServiceProvider services) : DbInitializer<FeedbackDbContext>(services);
