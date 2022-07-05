using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    public partial class ChatAuthorHasLeft : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "principal_ids",
                table: "chat_roles",
                newName: "author_ids");

            migrationBuilder.AddColumn<bool>(
                name: "has_left",
                table: "chat_authors",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_left",
                table: "chat_authors");

            migrationBuilder.RenameColumn(
                name: "author_ids",
                table: "chat_roles",
                newName: "principal_ids");
        }
    }
}
