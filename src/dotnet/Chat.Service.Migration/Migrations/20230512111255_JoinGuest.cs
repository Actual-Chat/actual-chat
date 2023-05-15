using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class JoinGuest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allowed_author_kind",
                table: "chats");

            migrationBuilder.AddColumn<bool>(
                name: "allow_anonymous_authors",
                table: "chats",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "allow_guest_authors",
                table: "chats",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allow_anonymous_authors",
                table: "chats");

            migrationBuilder.DropColumn(
                name: "allow_guest_authors",
                table: "chats");

            migrationBuilder.AddColumn<int>(
                name: "allowed_author_kind",
                table: "chats",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
