using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Chat
{
    public class ChatDbContext : DbContext
    {
        public DbSet<DbChatMessage> ChatMessages { get; protected set; } = null!;

        // Stl.Fusion.EntityFramework tables
        public DbSet<DbOperation> Operations { get; protected set; } = null!;

        public ChatDbContext(DbContextOptions options) : base(options) { }
    }
}
