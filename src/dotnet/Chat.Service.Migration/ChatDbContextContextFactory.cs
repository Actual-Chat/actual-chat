using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ActualChat.Chat;

public class ChatDbContextContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public string ConnectionString =
        "Server=localhost;Database=ac_dev_chat;Port=5432;User Id=postgres;Password=postgres;Include Error Detail=True";

    public ChatDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<ChatDbContext>();
        builder.UseNpgsql(
            ConnectionString,
            o => o.MigrationsAssembly(typeof(ChatDbContextContextFactory).Assembly.FullName));

        return new ChatDbContext(builder.Options);
    }
}
