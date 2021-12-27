using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations.Migrations
{
    public partial class AddLanguageSelection : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatEntries_ChatId_BeginsAt_EndsAt_Type",
                table: "ChatEntries");

            migrationBuilder.DropIndex(
                name: "IX_ChatEntries_ChatId_EndsAt_BeginsAt_Type",
                table: "ChatEntries");

            migrationBuilder.DropIndex(
                name: "IX_ChatEntries_ChatId_Id",
                table: "ChatEntries");

            migrationBuilder.DropIndex(
                name: "IX_ChatEntries_ChatId_Version",
                table: "ChatEntries");

            migrationBuilder.CreateTable(
                name: "ChatUserConfiguration",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ChatId = table.Column<string>(type: "text", nullable: false),
                    LocalId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Language = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatUserConfiguration", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Type_BeginsAt_EndsAt",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Type", "BeginsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Type_EndsAt_BeginsAt",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Type", "EndsAt", "BeginsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Type_Id",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Type", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Type_Version",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Type", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatUserConfiguration_ChatId_UserId",
                table: "ChatUserConfiguration",
                columns: new[] { "ChatId", "UserId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatUserConfiguration");

            migrationBuilder.DropIndex(
                name: "IX_ChatEntries_ChatId_Type_BeginsAt_EndsAt",
                table: "ChatEntries");

            migrationBuilder.DropIndex(
                name: "IX_ChatEntries_ChatId_Type_EndsAt_BeginsAt",
                table: "ChatEntries");

            migrationBuilder.DropIndex(
                name: "IX_ChatEntries_ChatId_Type_Id",
                table: "ChatEntries");

            migrationBuilder.DropIndex(
                name: "IX_ChatEntries_ChatId_Type_Version",
                table: "ChatEntries");

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_BeginsAt_EndsAt_Type",
                table: "ChatEntries",
                columns: new[] { "ChatId", "BeginsAt", "EndsAt", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_EndsAt_BeginsAt_Type",
                table: "ChatEntries",
                columns: new[] { "ChatId", "EndsAt", "BeginsAt", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Id",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Version",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Version" });
        }
    }
}
