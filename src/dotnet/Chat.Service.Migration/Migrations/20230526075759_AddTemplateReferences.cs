using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "template_id",
                table: "chats",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "templated_for_user_id",
                table: "chats",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "template_id",
                table: "chats");

            migrationBuilder.DropColumn(
                name: "templated_for_user_id",
                table: "chats");
        }
    }
}
