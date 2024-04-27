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
    public DbSet<DbEvent> Events { get; protected set; } = null!;

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ApplyConfigurationsFromAssembly(typeof(ChatDbContext).Assembly).UseSnakeCaseNaming();

        var chat = model.Entity<DbChat>();
        chat.Property(e => e.Id).UseCollation("C");
        chat.Property(e => e.MediaId).UseCollation("C");
        chat.Property(e => e.TemplateId).UseCollation("C");
        chat.Property(c => c.TemplatedForUserId).UseCollation("C");

        var chatEntry = model.Entity<DbChatEntry>();
        chatEntry.Property(e => e.Id).UseCollation("C");
        chatEntry.Property(e => e.ChatId).UseCollation("C");
        chatEntry.Property(e => e.AuthorId).UseCollation("C");
        chatEntry.Property(e => e.StreamId).UseCollation("C");
        chatEntry.Property(e => e.ForwardedAuthorId).UseCollation("C");
        chatEntry.Property(e => e.LinkPreviewId).UseCollation("C");

        var mention = model.Entity<DbMention>();
        mention.Property(e => e.Id).UseCollation("C");
        mention.Property(e => e.ChatId).UseCollation("C");
        mention.Property(e => e.MentionId).UseCollation("C");

        var reaction = model.Entity<DbReaction>();
        reaction.Property(e => e.Id).UseCollation("C");
        reaction.Property(e => e.AuthorId).UseCollation("C");
        reaction.Property(e => e.EntryId).UseCollation("C");

        var reactionSummary = model.Entity<DbReactionSummary>();
        reactionSummary.Property(e => e.Id).UseCollation("C");
        reactionSummary.Property(e => e.EntryId).UseCollation("C");

        var textEntryAttachment = model.Entity<DbTextEntryAttachment>();
        textEntryAttachment.Property(e => e.Id).UseCollation("C");
        textEntryAttachment.Property(e => e.EntryId).UseCollation("C");
        textEntryAttachment.Property(e => e.MediaId).UseCollation("C");
        textEntryAttachment.Property(a => a.ThumbnailMediaId).UseCollation("C");

        var authors = model.Entity<DbAuthor>();
        authors.Property(e => e.Id).UseCollation("C");
        authors.Property(e => e.ChatId).UseCollation("C");
        authors.Property(e => e.UserId).UseCollation("C");
        authors.Property(e => e.AvatarId).UseCollation("C");

        var role = model.Entity<DbRole>();
        role.Property(e => e.Id).UseCollation("C");
        role.Property(e => e.ChatId).UseCollation("C");

        var authorRole = model.Entity<DbAuthorRole>();
        authorRole.Property(e => e.DbAuthorId).UseCollation("C");
        authorRole.Property(e => e.DbRoleId).UseCollation("C");

        var chatCopyState = model.Entity<DbChatCopyState>();
        chatCopyState.Property(e => e.Id).UseCollation("C");
        chatCopyState.Property(e => e.SourceChatId).UseCollation("C");

        var operation = model.Entity<DbOperation>();
        operation.Property(e => e.Uuid).UseCollation("C");
        operation.Property(e => e.HostId).UseCollation("C");

        var events = model.Entity<DbEvent>();
        events.Property(e => e.Uuid).UseCollation("C");
    }
}
