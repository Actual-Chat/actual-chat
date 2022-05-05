using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Chat;

public class ChatDbContextContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_invite;Port=3306;User=root;Password=mariadb";

    public ChatDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ChatDbContext>();
        builder.UseMySql(ConnectionString,
            ServerVersion.AutoDetect(ConnectionString),
            o => o.MigrationsAssembly(typeof(ChatDbContextContextFactory).Assembly.FullName));

        return new ChatDbContext(builder.Options);
    }
}
