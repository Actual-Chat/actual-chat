using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class AddContentHashToChatEntryLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chat_entry_languages_id",
                table: "chat_entry_languages");

            migrationBuilder.AddColumn<string>(
                name: "entry_content_hash",
                table: "chat_entry_languages",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "entry_content_hash",
                table: "chat_entry_languages");

            migrationBuilder.CreateIndex(
                name: "ix_chat_entry_languages_id",
                table: "chat_entry_languages",
                column: "id",
                filter: "languages = ''");
        }
    }
}
