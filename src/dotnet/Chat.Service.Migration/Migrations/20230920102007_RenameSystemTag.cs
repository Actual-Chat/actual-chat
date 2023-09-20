using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class RenameSystemTag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allow_single_author_only",
                table: "chats");

            migrationBuilder.RenameColumn(
                name: "tag",
                table: "chats",
                newName: "system_tag");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "system_tag",
                table: "chats",
                newName: "tag");

            migrationBuilder.AddColumn<bool>(
                name: "allow_single_author_only",
                table: "chats",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
