using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Chat.Db;

public class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContextBase(options)
{
    public DbSet<DbChat> Chats { get; protected set; } = null!;
    public DbSet<DbChatEntry> ChatEntries { get; protected set; } = null!;
    public DbSet<DbChatEntryLanguage> ChatEntryLanguages { get; protected set; } = null!;
    public DbSet<DbTranslation> Translations { get; protected set; } = null!;
    public DbSet<DbMention> Mentions { get; protected set; } = null!;
    public DbSet<DbReaction> Reactions { get; protected set; } = null!;
    public DbSet<DbReactionSummary> ReactionSummaries { get; protected set; } = null!;
    public DbSet<DbChatEntryAttachment> ChatEntryAttachments { get; protected set; } = null!;
    public DbSet<DbChatVisualMediaItem> ChatVisualMediaItems { get; protected set; } = null!;
    public DbSet<DbChatFileItem> ChatFileItems { get; protected set; } = null!;
    public DbSet<DbChatLinkItem> ChatLinkItems { get; protected set; } = null!;
    public DbSet<DbAuthor> Authors { get; protected set; } = null!;
    public DbSet<DbRole> Roles { get; protected set; } = null!;
    public DbSet<DbAuthorRole> AuthorRoles { get; protected set; } = null!;
    public DbSet<DbChatCopyState> ChatCopyStates { get; protected set; } = null!;
    public DbSet<DbPlace> Places { get; protected set; } = null!;
    public DbSet<DbReadPositionsStat> ReadPositionsStats { get; protected set; } = null!;
    public DbSet<DbAlias> Aliases { get; protected set; } = null!;
    public DbSet<DbConversation> Conversations { get; protected set; } = null!;
    public DbSet<DbSharedLocation> SharedLocations { get; protected set; } = null!;

    // ActualLab.Fusion.EntityFramework tables
    public DbSet<DbOperation> Operations { get; protected set; } = null!;
    public DbSet<DbEvent> Events { get; protected set; } = null!;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Add(_ => new RemoveDbEventIndexesConvention());
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ApplyConfigurationsFromAssembly(typeof(ChatDbContext).Assembly).UseSnakeCaseNaming();

        var chat = model.Entity<DbChat>();
        chat.Property(e => e.Id).UseCollation("C");
        chat.Property(e => e.MediaId).UseCollation("C");
        chat.Property(e => e.TemplateId).UseCollation("C");
        chat.Property(c => c.TemplatedForUserId).UseCollation("C");
        chat.HasIndex(x => x.Version).IncludeProperties(nameof(DbChat.Id), nameof(DbChat.IsPlaceRootChat));

        var chatEntry = model.Entity<DbChatEntry>();
        chatEntry.Property(e => e.Id).UseCollation("C");
        chatEntry.Property(e => e.ChatId).UseCollation("C");
        chatEntry.Property(e => e.AuthorId).UseCollation("C");
        chatEntry.Property(e => e.ContentStreamId).UseCollation("C");
        chatEntry.Property(e => e.AudioId).UseCollation("C");
        chatEntry.Property(e => e.ForwardedAuthorId).UseCollation("C");
        chatEntry.Property(e => e.LinkPreviewIds).UseCollation("C");
        chatEntry.HasIndex(e => e.ContentStreamId).HasFilter("\"kind\" = 0 AND \"content_stream_id\" IS NOT NULL");
        chatEntry.HasIndex(e => e.AudioId).HasFilter("\"kind\" = 0 AND \"audio_id\" IS NOT NULL");

        var chatEntryLanguage = model.Entity<DbChatEntryLanguage>();
        chatEntryLanguage.Property(e => e.Id).UseCollation("C");

        var translation = model.Entity<DbTranslation>();
        translation.Property(e => e.Id).UseCollation("C");

        var mention = model.Entity<DbMention>();
        mention.Property(e => e.Id).UseCollation("C");
        mention.Property(e => e.ChatId).UseCollation("C");
        mention.Property(e => e.MentionRef).UseCollation("C");

        var reaction = model.Entity<DbReaction>();
        reaction.Property(e => e.Id).UseCollation("C");
        reaction.Property(e => e.AuthorId).UseCollation("C");
        reaction.Property(e => e.EntryId).UseCollation("C");

        var reactionSummary = model.Entity<DbReactionSummary>();
        reactionSummary.Property(e => e.Id).UseCollation("C");
        reactionSummary.Property(e => e.EntryId).UseCollation("C");

        var chatEntryAttachment = model.Entity<DbChatEntryAttachment>();
        chatEntryAttachment.Property(e => e.Id).UseCollation("C");
        chatEntryAttachment.Property(e => e.EntryId).UseCollation("C");
        chatEntryAttachment.Property(e => e.MediaId).UseCollation("C");
        chatEntryAttachment.Property(a => a.ThumbnailMediaId).UseCollation("C");

        var visualMedia = model.Entity<DbChatVisualMediaItem>();
        visualMedia.Property(e => e.Id).UseCollation("C");
        visualMedia.Property(e => e.ChatId).UseCollation("C");
        visualMedia.Property(e => e.EntryId).UseCollation("C");
        visualMedia.Property(e => e.MediaId).UseCollation("C");
        visualMedia.Property(e => e.ThumbnailMediaId).UseCollation("C");

        var fileItem = model.Entity<DbChatFileItem>();
        fileItem.Property(e => e.Id).UseCollation("C");
        fileItem.Property(e => e.ChatId).UseCollation("C");
        fileItem.Property(e => e.EntryId).UseCollation("C");
        fileItem.Property(e => e.MediaId).UseCollation("C");

        var linkItem = model.Entity<DbChatLinkItem>();
        linkItem.Property(e => e.Id).UseCollation("C");
        linkItem.Property(e => e.ChatId).UseCollation("C");
        linkItem.Property(e => e.EntryId).UseCollation("C");
        linkItem.Property(e => e.LinkPreviewId).UseCollation("C");

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

        var readPositionsStat = model.Entity<DbReadPositionsStat>();
        readPositionsStat.Property(e => e.ChatId).UseCollation("C");
        readPositionsStat.Property(e => e.Top1UserId).UseCollation("C");
        readPositionsStat.Property(e => e.Top2UserId).UseCollation("C");

        var alias = model.Entity<DbAlias>();
        alias.Property(e => e.Id).UseCollation("C");

        var conversation = model.Entity<DbConversation>();
        conversation.Property(e => e.Id).UseCollation("C");
        conversation.Property(e => e.Title).UseCollation("C");
        conversation.Property(e => e.Description).UseCollation("C");
        conversation.Property(e => e.Summary).UseCollation("C");
        conversation.Property(e => e.AuthorIds).UseCollation("C");
        conversation.HasIndex(x => new { x.ChatId, x.StartEntryLid }).IsUnique().IncludeProperties(nameof(DbConversation.EndEntryLid));
        conversation.HasIndex(x => new { x.ChatId, x.EndEntryLid }).IsDescending(false, true).IsUnique().IncludeProperties(nameof(DbConversation.StartEntryLid));

        var sharedLocation = model.Entity<DbSharedLocation>();
        sharedLocation.Property(e => e.Id).UseCollation("C");
        sharedLocation.Property(e => e.ChatId).UseCollation("C");
        sharedLocation.Property(e => e.AuthorId).UseCollation("C");
        sharedLocation.HasIndex(e => e.ChatId);

        var operation = model.Entity<DbOperation>();
        operation.Property(e => e.Uuid).UseCollation("C");
        operation.Property(e => e.HostId).UseCollation("C");

        var events = model.Entity<DbEvent>();
        events.Property(e => e.Uuid).UseCollation("C");
        events.DefineIndexes();
    }
}
