using ActualChat.Db;

namespace ActualChat.Feedback.Db;

public class FeedbackDbInitializer : DbInitializer<FeedbackDbContext>
{
    public FeedbackDbInitializer(IServiceProvider services) : base(services)
    { }
}
