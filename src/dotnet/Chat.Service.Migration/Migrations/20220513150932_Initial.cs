using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "_operations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    agent_id = table.Column<string>(type: "text", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    commit_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    command_json = table.Column<string>(type: "text", nullable: false),
                    items_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_authors",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    local_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_anonymous = table.Column<bool>(type: "boolean", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_authors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_entries",
                columns: table => new
                {
                    composite_id = table.Column<string>(type: "text", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_removed = table.Column<bool>(type: "boolean", nullable: false),
                    author_id = table.Column<string>(type: "text", nullable: false),
                    begins_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    client_side_begins_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    content_ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration = table.Column<double>(type: "double precision", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    has_attachments = table.Column<bool>(type: "boolean", nullable: false),
                    stream_id = table.Column<string>(type: "text", nullable: true),
                    audio_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    video_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    text_to_time_map = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_entries", x => x.composite_id);
                });

            migrationBuilder.CreateTable(
                name: "chats",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    chat_type = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "text_entry_attachments",
                columns: table => new
                {
                    composite_id = table.Column<string>(type: "text", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    entry_id = table.Column<long>(type: "bigint", nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    content_id = table.Column<string>(type: "text", nullable: false),
                    metadata_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_text_entry_attachments", x => x.composite_id);
                });

            migrationBuilder.CreateTable(
                name: "chat_owners",
                columns: table => new
                {
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    db_chat_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_owners", x => new { x.chat_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_chat_owners_chats_db_chat_id",
                        column: x => x.db_chat_id,
                        principalTable: "chats",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_commit_time",
                table: "_operations",
                column: "commit_time");

            migrationBuilder.CreateIndex(
                name: "ix_start_time",
                table: "_operations",
                column: "start_time");

            migrationBuilder.CreateIndex(
                name: "ix_chat_authors_chat_id_local_id",
                table: "chat_authors",
                columns: new[] { "chat_id", "local_id" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_authors_chat_id_user_id",
                table: "chat_authors",
                columns: new[] { "chat_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_type_begins_at_ends_at",
                table: "chat_entries",
                columns: new[] { "chat_id", "type", "begins_at", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_type_ends_at_begins_at",
                table: "chat_entries",
                columns: new[] { "chat_id", "type", "ends_at", "begins_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_type_id",
                table: "chat_entries",
                columns: new[] { "chat_id", "type", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_type_is_removed_id",
                table: "chat_entries",
                columns: new[] { "chat_id", "type", "is_removed", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id_type_version",
                table: "chat_entries",
                columns: new[] { "chat_id", "type", "version" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_owners_db_chat_id",
                table: "chat_owners",
                column: "db_chat_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_operations");

            migrationBuilder.DropTable(
                name: "chat_authors");

            migrationBuilder.DropTable(
                name: "chat_entries");

            migrationBuilder.DropTable(
                name: "chat_owners");

            migrationBuilder.DropTable(
                name: "text_entry_attachments");

            migrationBuilder.DropTable(
                name: "chats");
        }
    }
}
