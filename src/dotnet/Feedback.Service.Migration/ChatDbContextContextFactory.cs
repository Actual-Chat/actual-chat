using ActualChat.Feedback.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Feedback;

public class FeedbackDbContextContextFactory : IDesignTimeDbContextFactory<FeedbackDbContext>
{
    public string UsePostgreSql =
            "Server=localhost;Database=ac_dev_feedback;Port=5432;User Id=postgres;Password=postgres";

    public FeedbackDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FeedbackDbContext>();
        optionsBuilder.UseNpgsql(
            UsePostgreSql,
            o => o.MigrationsAssembly(typeof(FeedbackDbContextContextFactory).Assembly.FullName));

        return new FeedbackDbContext(optionsBuilder.Options);
    }
}
