﻿// <auto-generated />
using System;
using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    [DbContext(typeof(ChatDbContext))]
    [Migration("20240716115322_ReadPositionsStat")]
    partial class ReadPositionsStat
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.4")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("ActualChat.Chat.Db.DbAuthor", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id")
                        .UseCollation("C");

                    b.Property<string>("AvatarId")
                        .HasColumnType("text")
                        .HasColumnName("avatar_id")
                        .UseCollation("C");

                    b.Property<string>("ChatId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("chat_id")
                        .UseCollation("C");

                    b.Property<bool>("HasLeft")
                        .HasColumnType("boolean")
                        .HasColumnName("has_left");

                    b.Property<bool>("IsAnonymous")
                        .HasColumnType("boolean")
                        .HasColumnName("is_anonymous");

                    b.Property<long>("LocalId")
                        .HasColumnType("bigint")
                        .HasColumnName("local_id");

                    b.Property<string>("UserId")
                        .HasColumnType("text")
                        .HasColumnName("user_id")
                        .UseCollation("C");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_authors");

                    b.HasIndex("ChatId", "LocalId")
                        .IsUnique()
                        .HasDatabaseName("ix_authors_chat_id_local_id");

                    b.HasIndex("ChatId", "UserId")
                        .IsUnique()
                        .HasDatabaseName("ix_authors_chat_id_user_id");

                    b.HasIndex("UserId", "AvatarId")
                        .HasDatabaseName("ix_authors_user_id_avatar_id");

                    b.ToTable("authors");

                    b.HasAnnotation("ConflictStrategy", ConflictStrategy.DoNothing);
                });

            modelBuilder.Entity("ActualChat.Chat.Db.DbAuthorRole", b =>
                {
                    b.Property<string>("DbAuthorId")
                        .HasColumnType("text")
                        .HasColumnName("author_id")
                        .UseCollation("C");

                    b.Property<string>("DbRoleId")
                        .HasColumnType("text")
                        .HasColumnName("role_id")
                        .UseCollation("C");

                    b.HasKey("DbAuthorId", "DbRoleId")
                        .HasName("pk_author_roles");

                    b.HasIndex("DbRoleId", "DbAuthorId")
                        .IsUnique()
                        .HasDatabaseName("ix_author_roles_role_id_author_id");

                    b.ToTable("author_roles");
                });

            modelBuilder.Entity("ActualChat.Chat.Db.DbChat", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id")
                        .UseCollation("C");

                    b.Property<bool>("AllowAnonymousAuthors")
                        .HasColumnType("boolean")
                        .HasColumnName("allow_anonymous_authors");

                    b.Property<bool>("AllowGuestAuthors")
                        .HasColumnType("boolean")
                        .HasColumnName("allow_guest_authors");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<bool>("IsArchived")
                        .HasColumnType("boolean")
                        .HasColumnName("is_archived");

                    b.Property<bool>("IsPublic")
                        .HasColumnType("boolean")
                        .HasColumnName("is_public");

                    b.Property<bool>("IsTemplate")
                        .HasColumnType("boolean")
                        .HasColumnName("is_template");

                    b.Property<int>("Kind")
                        .HasColumnType("integer")
                        .HasColumnName("kind");

                    b.Property<string>("MediaId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("media_id")
                        .UseCollation("C");

                    b.Property<string>("Picture")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("picture");

                    b.Property<string>("SystemTag")
                        .HasColumnType("text")
                        .HasColumnName("system_tag");

                    b.Property<string>("TemplateId")
                        .HasColumnType("text")
                        .HasColumnName("template_id")
                        .UseCollation("C");

                    b.Property<string>("TemplatedForUserId")
                        .HasColumnType("text")
                        .HasColumnName("templated_for_user_id")
                        .UseCollation("C");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("title");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_chats");

                    b.HasIndex("CreatedAt")
                        .HasDatabaseName("ix_chats_created_at");

                    b.HasIndex("Version", "Id")
                        .HasDatabaseName("ix_chats_version_id");

                    b.ToTable("chats");
                });

            modelBuilder.Entity("ActualChat.Chat.Db.DbChatCopyState", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id")
                        .UseCollation("C");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<bool>("IsCopiedSuccessfully")
                        .HasColumnType("boolean")
                        .HasColumnName("is_copied_successfully");

                    b.Property<bool>("IsPublished")
                        .HasColumnType("boolean")
                        .HasColumnName("is_published");

                    b.Property<DateTime>("LastCopyingAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("last_copying_at");

                    b.Property<string>("LastCorrelationId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("last_correlation_id");

                    b.Property<long>("LastEntryId")
                        .HasColumnType("bigint")
                        .HasColumnName("last_entry_id");

                    b.Property<DateTime>("PublishedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("published_at");

                    b.Property<string>("SourceChatId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("source_chat_id")
                        .UseCollation("C");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_chat_copy_states");

                    b.HasIndex("SourceChatId")
                        .HasDatabaseName("ix_chat_copy_states_source_chat_id");

                    b.ToTable("chat_copy_states");
                });

            modelBuilder.Entity("ActualChat.Chat.Db.DbChatEntry", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id")
                        .UseCollation("C");

                    b.Property<long?>("AudioEntryId")
                        .HasColumnType("bigint")
                        .HasColumnName("audio_entry_id");

                    b.Property<string>("AuthorId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("author_id")
                        .UseCollation("C");

                    b.Property<DateTime>("BeginsAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("begins_at");

                    b.Property<string>("ChatId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("chat_id")
                        .UseCollation("C");

                    b.Property<DateTime?>("ClientSideBeginsAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("client_side_begins_at");

                    b.Property<string>("Content")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("content");

                    b.Property<DateTime?>("ContentEndsAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("content_ends_at");

                    b.Property<double>("Duration")
                        .HasColumnType("double precision")
                        .HasColumnName("duration");

                    b.Property<DateTime?>("EndsAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("ends_at");

                    b.Property<string>("ForwardedAuthorId")
                        .HasColumnType("text")
                        .HasColumnName("forwarded_author_id")
                        .UseCollation("C");

                    b.Property<string>("ForwardedAuthorName")
                        .HasColumnType("text")
                        .HasColumnName("forwarded_author_name");

                    b.Property<DateTime?>("ForwardedChatEntryBeginsAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("forwarded_chat_entry_begins_at");

                    b.Property<string>("ForwardedChatEntryId")
                        .HasColumnType("text")
                        .HasColumnName("forwarded_chat_entry_id");

                    b.Property<string>("ForwardedChatTitle")
                        .HasColumnType("text")
                        .HasColumnName("forwarded_chat_title");

                    b.Property<bool>("HasAttachments")
                        .HasColumnType("boolean")
                        .HasColumnName("has_attachments");

                    b.Property<bool>("HasReactions")
                        .HasColumnType("boolean")
                        .HasColumnName("has_reactions");

                    b.Property<bool>("IsRemoved")
                        .HasColumnType("boolean")
                        .HasColumnName("is_removed");

                    b.Property<bool>("IsSystemEntry")
                        .HasColumnType("boolean")
                        .HasColumnName("is_system_entry");

                    b.Property<int>("Kind")
                        .HasColumnType("integer")
                        .HasColumnName("kind");

                    b.Property<string>("LinkPreviewId")
                        .HasColumnType("text")
                        .HasColumnName("link_preview_id")
                        .UseCollation("C");

                    b.Property<int?>("LinkPreviewMode")
                        .HasColumnType("integer")
                        .HasColumnName("link_preview_mode");

                    b.Property<long>("LocalId")
                        .HasColumnType("bigint")
                        .HasColumnName("local_id");

                    b.Property<long?>("RepliedChatEntryId")
                        .HasColumnType("bigint")
                        .HasColumnName("replied_chat_entry_id");

                    b.Property<string>("StreamId")
                        .HasColumnType("text")
                        .HasColumnName("stream_id")
                        .UseCollation("C");

                    b.Property<string>("TimeMap")
                        .HasColumnType("text")
                        .HasColumnName("time_map");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.Property<long?>("VideoEntryId")
                        .HasColumnType("bigint")
                        .HasColumnName("video_entry_id");

                    b.HasKey("Id")
                        .HasName("pk_chat_entries");

                    b.HasIndex("ChatId", "Kind", "LocalId")
                        .IsUnique()
                        .HasDatabaseName("ix_chat_entries_chat_id_kind_local_id");

                    b.HasIndex("ChatId", "Kind", "Version")
                        .HasDatabaseName("ix_chat_entries_chat_id_kind_version");

                    b.HasIndex("ChatId", "Kind", "BeginsAt", "EndsAt")
                        .HasDatabaseName("ix_chat_entries_chat_id_kind_begins_at_ends_at");

                    b.HasIndex("ChatId", "Kind", "EndsAt", "BeginsAt")
                        .HasDatabaseName("ix_chat_entries_chat_id_kind_ends_at_begins_at");

                    b.HasIndex("ChatId", "Kind", "IsRemoved", "LocalId")
                        .HasDatabaseName("ix_chat_entries_chat_id_kind_is_removed_local_id");

                    b.ToTable("chat_entries");
                });

            modelBuilder.Entity("ActualChat.Chat.Db.DbMention", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id")
                        .UseCollation("C");

                    b.Property<string>("ChatId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("chat_id")
                        .UseCollation("C");

                    b.Property<long>("EntryId")
                        .HasColumnType("bigint")
                        .HasColumnName("entry_id");

                    b.Property<string>("MentionId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("mention_id")
                        .UseCollation("C");

                    b.HasKey("Id")
                        .HasName("pk_mentions");

                    b.HasIndex("ChatId", "EntryId", "MentionId")
                        .HasDatabaseName("ix_mentions_chat_id_entry_id_mention_id");

                    b.HasIndex("ChatId", "MentionId", "EntryId")
                        .HasDatabaseName("ix_mentions_chat_id_mention_id_entry_id");

                    b.ToTable("mentions");
                });

            modelBuilder.Entity("ActualChat.Chat.Db.DbPlace", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id");

                    b.Property<string>("BackgroundMediaId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("background_media_id");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("created_at");

                    b.Property<bool>("IsPublic")
                        .HasColumnType("boolean")
                        .HasColumnName("is_public");

                    b.Property<string>("MediaId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("media_id");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("title");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_places");

                    b.HasIndex("CreatedAt")
                        .HasDatabaseName("ix_places_created_at");

                    b.HasIndex("Version", "Id")
                        .HasDatabaseName("ix_places_version_id");

                    b.ToTable("places");
                });

            modelBuilder.Entity("ActualChat.Chat.Db.DbReaction", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id")
                        .UseCollation("C");

                    b.Property<string>("AuthorId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("author_id")
                        .UseCollation("C");

                    b.Property<string>("EmojiId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("emoji_id");

                    b.Property<string>("EntryId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("entry_id")
                        .UseCollation("C");

                    b.Property<DateTime>("ModifiedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("modified_at");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_reactions");

                    b.ToTable("reactions");
                });

            modelBuilder.Entity("ActualChat.Chat.Db.DbReactionSummary", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id")
                        .UseCollation("C");

                    b.Property<long>("Count")
                        .HasColumnType("bigint")
                        .HasColumnName("count");

                    b.Property<string>("EmojiId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("emoji_id");

                    b.Property<string>("EntryId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("entry_id")
                        .UseCollation("C");

                    b.Property<string>("FirstAuthorIdsJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("first_author_ids_json");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_reaction_summaries");

                    b.HasIndex("EntryId")
                        .HasDatabaseName("ix_reaction_summaries_entry_id");

                    b.ToTable("reaction_summaries");
                });

            modelBuilder.Entity("ActualChat.Chat.Db.DbReadPositionsStat", b =>
                {
                    b.Property<string>("ChatId")
                        .HasColumnType("text")
                        .HasColumnName("chat_id")
                        .UseCollation("C");

                    b.Property<long>("StartTrackingEntryLid")
                        .HasColumnType("bigint")
                        .HasColumnName("start_tracking_entry_lid");

                    b.Property<long>("Top1EntryLid")
                        .HasColumnType("bigint")
                        .HasColumnName("top1_entry_lid");

                    b.Property<string>("Top1UserId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("top1_user_id")
                        .UseCollation("C");

                    b.Property<long>("Top2EntryLid")
                        .HasColumnType("bigint")
                        .HasColumnName("top2_entry_lid");

                    b.Property<string>("Top2UserId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("top2_user_id")
                        .UseCollation("C");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("ChatId")
                        .HasName("pk_read_positions_stat");

                    b.ToTable("read_positions_stat");
                });

            modelBuilder.Entity("ActualChat.Chat.Db.DbRole", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id")
                        .UseCollation("C");

                    b.Property<bool>("CanEditProperties")
                        .HasColumnType("boolean")
                        .HasColumnName("can_edit_properties");

                    b.Property<bool>("CanEditRoles")
                        .HasColumnType("boolean")
                        .HasColumnName("can_edit_roles");

                    b.Property<bool>("CanInvite")
                        .HasColumnType("boolean")
                        .HasColumnName("can_invite");

                    b.Property<bool>("CanJoin")
                        .HasColumnType("boolean")
                        .HasColumnName("can_join");

                    b.Property<bool>("CanLeave")
                        .HasColumnType("boolean")
                        .HasColumnName("can_leave");

                    b.Property<bool>("CanRead")
                        .HasColumnType("boolean")
                        .HasColumnName("can_read");

                    b.Property<bool>("CanSeeMembers")
                        .HasColumnType("boolean")
                        .HasColumnName("can_see_members");

                    b.Property<bool>("CanWrite")
                        .HasColumnType("boolean")
                        .HasColumnName("can_write");

                    b.Property<string>("ChatId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("chat_id")
                        .UseCollation("C");

                    b.Property<long>("LocalId")
                        .HasColumnType("bigint")
                        .HasColumnName("local_id");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.Property<string>("Picture")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("picture");

                    b.Property<short>("SystemRole")
                        .HasColumnType("smallint")
                        .HasColumnName("system_role");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_roles");

                    b.HasIndex("ChatId", "LocalId")
                        .IsUnique()
                        .HasDatabaseName("ix_roles_chat_id_local_id");

                    b.HasIndex("ChatId", "Name")
                        .HasDatabaseName("ix_roles_chat_id_name");

                    b.ToTable("roles");
                });

            modelBuilder.Entity("ActualChat.Chat.Db.DbTextEntryAttachment", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text")
                        .HasColumnName("id")
                        .UseCollation("C");

                    b.Property<string>("ContentId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("content_id");

                    b.Property<string>("EntryId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("entry_id")
                        .UseCollation("C");

                    b.Property<int>("Index")
                        .HasColumnType("integer")
                        .HasColumnName("index");

                    b.Property<string>("MediaId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("media_id")
                        .UseCollation("C");

                    b.Property<string>("MetadataJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("metadata_json");

                    b.Property<string>("ThumbnailMediaId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("thumbnail_media_id")
                        .UseCollation("C");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Id")
                        .HasName("pk_text_entry_attachments");

                    b.ToTable("text_entry_attachments");
                });

            modelBuilder.Entity("ActualLab.Fusion.EntityFramework.Operations.DbEvent", b =>
                {
                    b.Property<string>("Uuid")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("text")
                        .HasColumnName("uuid")
                        .UseCollation("C");

                    b.Property<DateTime>("DelayUntil")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("delay_until");

                    b.Property<DateTime>("LoggedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("logged_at");

                    b.Property<int>("State")
                        .HasColumnType("integer")
                        .HasColumnName("state");

                    b.Property<string>("ValueJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("value_json");

                    b.Property<long>("Version")
                        .IsConcurrencyToken()
                        .HasColumnType("bigint")
                        .HasColumnName("version");

                    b.HasKey("Uuid")
                        .HasName("pk_events");

                    b.HasIndex("DelayUntil")
                        .HasDatabaseName("ix_events_delay_until");

                    b.HasIndex("Uuid")
                        .IsUnique()
                        .HasDatabaseName("ix_events_uuid");

                    b.HasIndex("State", "DelayUntil")
                        .HasDatabaseName("ix_events_state_delay_until");

                    b.ToTable("_events");
                });

            modelBuilder.Entity("ActualLab.Fusion.EntityFramework.Operations.DbOperation", b =>
                {
                    b.Property<long>("Index")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .HasColumnName("index");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Index"));

                    b.Property<string>("CommandJson")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("command_json");

                    b.Property<string>("HostId")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("host_id")
                        .UseCollation("C");

                    b.Property<string>("ItemsJson")
                        .HasColumnType("text")
                        .HasColumnName("items_json");

                    b.Property<DateTime>("LoggedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("logged_at");

                    b.Property<string>("NestedOperations")
                        .HasColumnType("text")
                        .HasColumnName("nested_operations");

                    b.Property<string>("Uuid")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("uuid")
                        .UseCollation("C");

                    b.HasKey("Index")
                        .HasName("pk_operations");

                    b.HasIndex("LoggedAt")
                        .HasDatabaseName("ix_operations_logged_at");

                    b.HasIndex("Uuid")
                        .IsUnique()
                        .HasDatabaseName("ix_operations_uuid");

                    b.ToTable("_operations");
                });

            modelBuilder.Entity("ActualChat.Chat.Db.DbAuthorRole", b =>
                {
                    b.HasOne("ActualChat.Chat.Db.DbAuthor", null)
                        .WithMany("Roles")
                        .HasForeignKey("DbAuthorId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired()
                        .HasConstraintName("fk_author_roles_authors_author_id");
                });

            modelBuilder.Entity("ActualChat.Chat.Db.DbAuthor", b =>
                {
                    b.Navigation("Roles");
                });
#pragma warning restore 612, 618
        }
    }
}
