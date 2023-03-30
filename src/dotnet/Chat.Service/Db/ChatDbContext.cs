using ActualChat.Db;
using ActualChat.Medias.Db;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Operations;

namespace ActualChat.Chat.Db;

public class ChatDbContext : DbContextBase
{
    public DbSet<DbChat> Chats { get; protected set; } = null!;
    public DbSet<DbChatEntry> ChatEntries { get; protected set; } = null!;
    public DbSet<DbMention> Mentions { get; protected set; } = null!;
    public DbSet<DbReaction> Reactions { get; protected set; } = null!;
    public DbSet<DbReactionSummary> ReactionSummaries { get; protected set; } = null!;
    public DbSet<DbTextEntryAttachment> TextEntryAttachments { get; protected set; } = null!;
    public DbSet<DbAuthor> Authors { get; protected set; } = null!;
    public DbSet<DbRole> Roles { get; protected set; } = null!;
    public DbSet<DbAuthorRole> AuthorRoles { get; protected set; } = null!;
    public DbSet<DbMedia> Medias { get; protected set; } = null!;

    // Stl.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    public ChatDbContext(DbContextOptions options) : base(options) { }

#pragma warning disable IL2026
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ApplyConfigurationsFromAssembly(typeof(ChatDbContext).Assembly).UseSnakeCaseNaming();
        model.ApplyConfigurationsFromAssembly(typeof(DbMedia).Assembly).UseSnakeCaseNaming();
    }
#pragma warning restore IL2026
}
