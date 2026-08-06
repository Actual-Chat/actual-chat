using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "_events",
                columns: table => new
                {
                    uuid = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    logged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    delay_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    value_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.uuid);
                });

            migrationBuilder.CreateTable(
                name: "_operations",
                columns: table => new
                {
                    index = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    uuid = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    host_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    logged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    command_json = table.Column<string>(type: "text", nullable: false),
                    items_json = table.Column<string>(type: "text", nullable: true),
                    nested_operations = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => x.index);
                });

            migrationBuilder.CreateTable(
                name: "aliases",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aliases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "authors",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    local_id = table.Column<long>(type: "bigint", nullable: false),
                    is_anonymous = table.Column<bool>(type: "boolean", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    avatar_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    has_left = table.Column<bool>(type: "boolean", nullable: false),
                    is_place_author = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_authors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_copy_states",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    source_chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    is_copied_successfully = table.Column<bool>(type: "boolean", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    last_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    last_correlation_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_copying_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_copy_states", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_entries",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    local_id = table.Column<long>(type: "bigint", nullable: false),
                    is_removed = table.Column<bool>(type: "boolean", nullable: false),
                    author_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    replied_chat_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    is_system_entry = table.Column<bool>(type: "boolean", nullable: false),
                    is_thread_start_entry = table.Column<bool>(type: "boolean", nullable: false),
                    is_thread_entry = table.Column<bool>(type: "boolean", nullable: false),
                    has_attachment_uploads = table.Column<bool>(type: "boolean", nullable: false),
                    client_id = table.Column<string>(type: "text", nullable: false),
                    forwarded_chat_title = table.Column<string>(type: "text", nullable: true),
                    forwarded_author_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    forwarded_author_name = table.Column<string>(type: "text", nullable: true),
                    forwarded_chat_entry_id = table.Column<string>(type: "text", nullable: true),
                    forwarded_chat_entry_begins_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    begins_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    client_side_begins_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    content_ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration = table.Column<double>(type: "double precision", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: true),
                    has_attachments = table.Column<bool>(type: "boolean", nullable: false),
                    has_reactions = table.Column<bool>(type: "boolean", nullable: false),
                    link_preview_ids = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    link_preview_mode = table.Column<int>(type: "integer", nullable: true),
                    stream_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    audio_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    video_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    time_map = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_entry_languages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    languages = table.Column<string>(type: "text", nullable: false),
                    entry_content_hash = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_entry_languages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_roulettes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    profile_id1 = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    profile_id2 = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    user_id1 = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    user_id2 = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    initiated_by_user_id = table.Column<string>(type: "text", nullable: false),
                    completed_by_user_id = table.Column<string>(type: "text", nullable: false),
                    complete_reason = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_roulettes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chats",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    media_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    alias_id = table.Column<string>(type: "text", nullable: false),
                    is_template = table.Column<bool>(type: "boolean", nullable: false),
                    template_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    templated_for_user_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    allow_guest_authors = table.Column<bool>(type: "boolean", nullable: false),
                    allow_anonymous_authors = table.Column<bool>(type: "boolean", nullable: false),
                    system_tag = table.Column<string>(type: "text", nullable: true),
                    is_place_root_chat = table.Column<bool>(type: "boolean", nullable: false),
                    is_summarized = table.Column<bool>(type: "boolean", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    description = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    summary = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    start_entry_lid = table.Column<long>(type: "bigint", nullable: false),
                    end_entry_lid = table.Column<long>(type: "bigint", nullable: false),
                    message_count = table.Column<int>(type: "integer", nullable: false),
                    attachment_count = table.Column<int>(type: "integer", nullable: false),
                    starts_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    author_ids = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    attachment_ids = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mentions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    mention_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    entry_local_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mentions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "places",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    media_id = table.Column<string>(type: "text", nullable: false),
                    background_media_id = table.Column<string>(type: "text", nullable: false),
                    alias_id = table.Column<string>(type: "text", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_places", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reaction_summaries",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    entry_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    count = table.Column<long>(type: "bigint", nullable: false),
                    emoji = table.Column<string>(type: "text", nullable: false),
                    first_author_ids_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reaction_summaries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reactions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    author_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    entry_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    emoji = table.Column<string>(type: "text", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "read_positions_stat",
                columns: table => new
                {
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    start_tracking_entry_lid = table.Column<long>(type: "bigint", nullable: false),
                    top1_entry_lid = table.Column<long>(type: "bigint", nullable: false),
                    top1_user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    top2_entry_lid = table.Column<long>(type: "bigint", nullable: false),
                    top2_user_id = table.Column<string>(type: "text", nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_read_positions_stat", x => x.chat_id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    local_id = table.Column<long>(type: "bigint", nullable: false),
                    system_role = table.Column<short>(type: "smallint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    picture = table.Column<string>(type: "text", nullable: false),
                    can_read = table.Column<bool>(type: "boolean", nullable: false),
                    can_write = table.Column<bool>(type: "boolean", nullable: false),
                    can_join = table.Column<bool>(type: "boolean", nullable: false),
                    can_leave = table.Column<bool>(type: "boolean", nullable: false),
                    can_invite = table.Column<bool>(type: "boolean", nullable: false),
                    can_edit_properties = table.Column<bool>(type: "boolean", nullable: false),
                    can_edit_roles = table.Column<bool>(type: "boolean", nullable: false),
                    can_see_members = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "text_entry_attachments",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    entry_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    media_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    thumbnail_media_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    index = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_text_entry_attachments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "translations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    source_content_hash = table.Column<string>(type: "text", nullable: false),
                    stream_id = table.Column<string>(type: "text", nullable: true),
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    entry_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "author_roles",
                columns: table => new
                {
                    author_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    role_id = table.Column<string>(type: "text", nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_author_roles", x => new { x.author_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_author_roles_authors_author_id",
                        column: x => x.author_id,
                        principalTable: "authors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_delay_until_state_non_new",
                table: "_events",
                columns: new[] { "delay_until", "state" },
                filter: "state != 0");

            migrationBuilder.CreateIndex(
                name: "ix_events_pending",
                table: "_events",
                column: "delay_until",
                filter: "state = 0");

            migrationBuilder.CreateIndex(
                name: "ix_operations_logged_at",
                table: "_operations",
                column: "logged_at");

            migrationBuilder.CreateIndex(
                name: "ix_operations_uuid",
                table: "_operations",
                column: "uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_author_roles_role_id_author_id",
                table: "author_roles",
                columns: new[] { "role_id", "author_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_authors_chat_id_local_id",
                table: "authors",
                columns: new[] { "chat_id", "local_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_authors_chat_id_user_id",
                table: "authors",
                columns: new[] { "chat_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_authors_user_id_avatar_id",
                table: "authors",
                columns: new[] { "user_id", "avatar_id" });

            migrationBuilder.CreateIndex(
                name: "ix_authors_version_is_place_author",
                table: "authors",
                columns: new[] { "version", "is_place_author" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_copy_states_source_chat_id",
                table: "chat_copy_states",
                column: "source_chat_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_kind_begins_at_ends_at",
                table: "chat_entries",
                columns: new[] { "chat_id", "kind", "begins_at", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_kind_ends_at_begins_at",
                table: "chat_entries",
                columns: new[] { "chat_id", "kind", "ends_at", "begins_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_kind_is_removed_local_id",
                table: "chat_entries",
                columns: new[] { "chat_id", "kind", "is_removed", "local_id" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_kind_local_id",
                table: "chat_entries",
                columns: new[] { "chat_id", "kind", "local_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_kind_version",
                table: "chat_entries",
                columns: new[] { "chat_id", "kind", "version" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_version_local_id",
                table: "chat_entries",
                columns: new[] { "chat_id", "version", "local_id" },
                filter: "kind = 0");

            migrationBuilder.CreateIndex(
                name: "IX_chat_entries_stream_id",
                table: "chat_entries",
                column: "stream_id",
                filter: "\"kind\" = 0 AND \"stream_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_chat_roulettes_chat_id",
                table: "chat_roulettes",
                column: "chat_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_roulettes_profile_id1_profile_id2",
                table: "chat_roulettes",
                columns: new[] { "profile_id1", "profile_id2" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chats_created_at",
                table: "chats",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_chats_version",
                table: "chats",
                column: "version")
                .Annotation("Npgsql:IndexInclude", new[] { "id", "is_place_root_chat" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_chat_id_end_entry_lid",
                table: "conversations",
                columns: new[] { "chat_id", "end_entry_lid" },
                unique: true,
                descending: new[] { false, true })
                .Annotation("Npgsql:IndexInclude", new[] { "start_entry_lid" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_chat_id_start_entry_lid",
                table: "conversations",
                columns: new[] { "chat_id", "start_entry_lid" },
                unique: true)
                .Annotation("Npgsql:IndexInclude", new[] { "end_entry_lid" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_version",
                table: "conversations",
                column: "version");

            migrationBuilder.CreateIndex(
                name: "ix_mentions_chat_id_entry_local_id_mention_id",
                table: "mentions",
                columns: new[] { "chat_id", "entry_local_id", "mention_id" });

            migrationBuilder.CreateIndex(
                name: "ix_mentions_chat_id_mention_id_entry_local_id",
                table: "mentions",
                columns: new[] { "chat_id", "mention_id", "entry_local_id" });

            migrationBuilder.CreateIndex(
                name: "ix_places_created_at",
                table: "places",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_places_version_id",
                table: "places",
                columns: new[] { "version", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_reaction_summaries_entry_id",
                table: "reaction_summaries",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_chat_id_local_id",
                table: "roles",
                columns: new[] { "chat_id", "local_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_chat_id_name",
                table: "roles",
                columns: new[] { "chat_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_translations_stream_id_modified_at",
                table: "translations",
                columns: new[] { "stream_id", "modified_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_events");

            migrationBuilder.DropTable(
                name: "_operations");

            migrationBuilder.DropTable(
                name: "aliases");

            migrationBuilder.DropTable(
                name: "author_roles");

            migrationBuilder.DropTable(
                name: "chat_copy_states");

            migrationBuilder.DropTable(
                name: "chat_entries");

            migrationBuilder.DropTable(
                name: "chat_entry_languages");

            migrationBuilder.DropTable(
                name: "chat_roulettes");

            migrationBuilder.DropTable(
                name: "chats");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "mentions");

            migrationBuilder.DropTable(
                name: "places");

            migrationBuilder.DropTable(
                name: "reaction_summaries");

            migrationBuilder.DropTable(
                name: "reactions");

            migrationBuilder.DropTable(
                name: "read_positions_stat");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "text_entry_attachments");

            migrationBuilder.DropTable(
                name: "translations");

            migrationBuilder.DropTable(
                name: "authors");
        }
    }
}
