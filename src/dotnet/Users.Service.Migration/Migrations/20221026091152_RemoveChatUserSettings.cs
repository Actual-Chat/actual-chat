using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    public partial class RemoveChatUserSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_user_settings");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_user_settings",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    avatar_id = table.Column<string>(type: "text", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_user_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_user_settings_chat_id_user_id",
                table: "chat_user_settings",
                columns: new[] { "chat_id", "user_id" });
        }
    }
}
