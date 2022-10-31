using ActualChat.Feedback.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Feedback;

public class FeedbackDbContextContextFactory : IDesignTimeDbContextFactory<FeedbackDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_feedback;Port=5432;User Id=postgres;Password=postgres;Include Error Detail=True";

    public FeedbackDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<FeedbackDbContext>();
        builder.UseNpgsql(
            ConnectionString,
            o => o.MigrationsAssembly(typeof(FeedbackDbContextContextFactory).Assembly.FullName));

        return new FeedbackDbContext(builder.Options);
    }
}
