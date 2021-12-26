using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations.Migrations
{
    public partial class MoveUserChatSettingsToUsersDb : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatUserSettings");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatUserSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ChatId = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatUserSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatUserSettings_ChatId_UserId",
                table: "ChatUserSettings",
                columns: new[] { "ChatId", "UserId" });
        }
    }
}
