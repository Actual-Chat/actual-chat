using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations.Migrations
{
    public partial class UpdateChatUserSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocalId",
                table: "ChatUserConfiguration");
            migrationBuilder.RenameTable(name: "ChatUserConfiguration", newName: "ChatUserSettings");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "ChatUserSettings", newName: "ChatUserConfiguration");
            migrationBuilder.AddColumn<long>(
                name: "LocalId",
                table: "ChatUserConfiguration",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }
    }
}
