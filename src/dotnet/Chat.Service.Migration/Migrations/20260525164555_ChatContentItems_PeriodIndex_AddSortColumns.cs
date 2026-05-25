using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class ChatContentItems_PeriodIndex_AddSortColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chat_content_items_chat_id_kind_at",
                table: "chat_content_items");

            migrationBuilder.CreateIndex(
                name: "ix_chat_content_items_chat_id_kind_at_entry_local_id_local_ind~",
                table: "chat_content_items",
                columns: new[] { "chat_id", "kind", "at", "entry_local_id", "local_index" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chat_content_items_chat_id_kind_at_entry_local_id_local_ind~",
                table: "chat_content_items");

            migrationBuilder.CreateIndex(
                name: "ix_chat_content_items_chat_id_kind_at",
                table: "chat_content_items",
                columns: new[] { "chat_id", "kind", "at" });
        }
    }
}
