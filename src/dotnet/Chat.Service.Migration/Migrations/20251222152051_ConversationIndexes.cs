using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class ConversationIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_conversations_chat_id_end_entry_lid",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_chat_id_start_entry_lid",
                table: "conversations");
        }
    }
}
