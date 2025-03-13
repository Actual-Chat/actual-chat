using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class ChatEntryLanguageDetectionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id",
                table: "chat_entries",
                column: "chat_id",
                filter: "kind = 0 and not is_system_entry and not is_removed and languages is not null and languages != '' and  content != ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chat_entries_chat_id",
                table: "chat_entries");
        }
    }
}
