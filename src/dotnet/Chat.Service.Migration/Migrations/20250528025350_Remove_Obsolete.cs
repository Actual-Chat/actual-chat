using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Remove_Obsolete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "content_id",
                table: "text_entry_attachments");

            migrationBuilder.DropColumn(
                name: "metadata_json",
                table: "text_entry_attachments");

            migrationBuilder.DropColumn(
                name: "picture",
                table: "chats");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "content_id",
                table: "text_entry_attachments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "metadata_json",
                table: "text_entry_attachments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "picture",
                table: "chats",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
