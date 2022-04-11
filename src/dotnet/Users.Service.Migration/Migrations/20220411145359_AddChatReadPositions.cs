using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations.Migrations
{
    public partial class AddChatReadPositions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatReadPositions",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ChatId = table.Column<string>(type: "text", nullable: false),
                    EntryId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatReadPositions", x => new { x.UserId, x.ChatId });
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatReadPositions");
        }
    }
}
