using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Split_ChatContentItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_content_items");

            migrationBuilder.CreateTable(
                name: "chat_file_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    entry_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    entry_local_id = table.Column<long>(type: "bigint", nullable: false),
                    local_index = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    media_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    blob_id = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_file_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_link_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    entry_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    entry_local_id = table.Column<long>(type: "bigint", nullable: false),
                    local_index = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    link_preview_id = table.Column<string>(type: "text", nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_link_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_visual_media_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    entry_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    entry_local_id = table.Column<long>(type: "bigint", nullable: false),
                    local_index = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    media_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    blob_id = table.Column<string>(type: "text", nullable: false),
                    thumbnail_media_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    thumbnail_blob_id = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_visual_media_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_file_items_chat_id_at_entry_local_id_local_index",
                table: "chat_file_items",
                columns: new[] { "chat_id", "at", "entry_local_id", "local_index" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_file_items_entry_id",
                table: "chat_file_items",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_link_items_chat_id_at_entry_local_id_local_index",
                table: "chat_link_items",
                columns: new[] { "chat_id", "at", "entry_local_id", "local_index" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_link_items_entry_id",
                table: "chat_link_items",
                column: "entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_visual_media_items_chat_id_at_entry_local_id_local_ind~",
                table: "chat_visual_media_items",
                columns: new[] { "chat_id", "at", "entry_local_id", "local_index" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_visual_media_items_entry_id",
                table: "chat_visual_media_items",
                column: "entry_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_file_items");

            migrationBuilder.DropTable(
                name: "chat_link_items");

            migrationBuilder.DropTable(
                name: "chat_visual_media_items");

            migrationBuilder.CreateTable(
                name: "chat_content_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    blob_id = table.Column<string>(type: "text", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    entry_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    entry_local_id = table.Column<long>(type: "bigint", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    link_preview_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    local_index = table.Column<int>(type: "integer", nullable: false),
                    media_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    thumbnail_blob_id = table.Column<string>(type: "text", nullable: false),
                    thumbnail_media_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_content_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_content_items_chat_id_kind_at_entry_local_id_local_ind~",
                table: "chat_content_items",
                columns: new[] { "chat_id", "kind", "at", "entry_local_id", "local_index" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_content_items_entry_id",
                table: "chat_content_items",
                column: "entry_id");
        }
    }
}
