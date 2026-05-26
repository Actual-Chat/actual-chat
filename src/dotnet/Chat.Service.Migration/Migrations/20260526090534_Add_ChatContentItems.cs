using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Add_ChatContentItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_content_items",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    entry_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    entry_local_id = table.Column<long>(type: "bigint", nullable: false),
                    local_index = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    media_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    blob_id = table.Column<string>(type: "text", nullable: false),
                    thumbnail_media_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    thumbnail_blob_id = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    link_preview_id = table.Column<string>(type: "text", nullable: false, collation: "C")
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_content_items");
        }
    }
}
