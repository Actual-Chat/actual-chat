using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Chat.Db;

public class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContextBase(options)
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
    public DbSet<DbChatCopyState> ChatCopyStates { get; protected set; } = null!;

    // ActualLab.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder model)
        => model.ApplyConfigurationsFromAssembly(typeof(ChatDbContext).Assembly).UseSnakeCaseNaming();
}
