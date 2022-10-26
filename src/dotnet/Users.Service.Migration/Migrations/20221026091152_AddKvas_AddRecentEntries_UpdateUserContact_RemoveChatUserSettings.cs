using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    public partial class AddKvas_AddRecentEntries_UpdateUserContact_RemoveChatUserSettings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_user_settings");

            migrationBuilder.AddColumn<bool>(
                name: "is_favorite",
                table: "user_contacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "kvas_entries",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kvas_entries", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "recent_entries",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    shard_key = table.Column<string>(type: "text", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recent_entries", x => x.id);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kvas_entries");

            migrationBuilder.DropTable(
                name: "recent_entries");

            migrationBuilder.DropColumn(
                name: "is_favorite",
                table: "user_contacts");

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
