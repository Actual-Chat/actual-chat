using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class ForwardMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "forwarded_author_id",
                table: "chat_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "forwarded_chat_entry_id",
                table: "chat_entries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "forwarded_author_id",
                table: "chat_entries");

            migrationBuilder.DropColumn(
                name: "forwarded_chat_entry_id",
                table: "chat_entries");
        }
    }
}
