using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Chat.Migrations;

public class ChatDbContextContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public string UsePostgreSql =
            "Server=localhost;Database=ac_dev_chat;Port=5432;User Id=postgres;Password=postgres";

    public ChatDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ChatDbContext>();
        optionsBuilder.UseNpgsql(
            UsePostgreSql,
            o => o.MigrationsAssembly(typeof(ChatDbContextContextFactory).Assembly.FullName));

        return new ChatDbContext(optionsBuilder.Options);
    }
}
