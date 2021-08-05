using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat
{
    public class ChatDbContext : DbContext
    {
        public ChatDbContext(DbContextOptions options) : base(options) { }
    }
}
