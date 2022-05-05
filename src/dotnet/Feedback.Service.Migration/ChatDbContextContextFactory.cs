using ActualChat.Feedback.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Feedback;

public class FeedbackDbContextContextFactory : IDesignTimeDbContextFactory<FeedbackDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_invite;Port=3306;User=root;Password=mariadb";

    public FeedbackDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<FeedbackDbContext>();
        builder.UseMySql(ConnectionString,
            ServerVersion.AutoDetect(ConnectionString),
            o => o.MigrationsAssembly(typeof(FeedbackDbContextContextFactory).Assembly.FullName));

        return new FeedbackDbContext(builder.Options);
    }
}
