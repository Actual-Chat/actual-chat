using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Chat.Db;

public class ChatDbContext : DbContextBase
{
    public DbSet<DbChat> Chats { get; protected set; } = null!;
    public DbSet<DbChatEntry> ChatEntries { get; protected set; } = null!;
    public DbSet<DbMention> Mentions { get; protected set; } = null!;
    public DbSet<DbTextEntryAttachment> TextEntryAttachments { get; protected set; } = null!;
    public DbSet<DbChatOwner> ChatOwners { get; protected set; } = null!;
    public DbSet<DbChatAuthor> ChatAuthors { get; protected set; } = null!;
    public DbSet<DbChatRole> ChatRoles { get; protected set; } = null!;
    public DbSet<DbChatAuthorRole> ChatAuthorRoles { get; protected set; } = null!;

    // Stl.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    public ChatDbContext(DbContextOptions options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(ChatDbContext).Assembly).UseSnakeCaseNaming();
}
