using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notification.Migrations
{
    public partial class RenameNotificationsUserId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.RenameColumn(
                name: "chat_user_id",
                table: "notifications",
                newName: "chat_author_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "chat_author_id",
                table: "notifications",
                newName: "chat_user_id");

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    accessed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    chat_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    chat_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    device_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_messages_device_id",
                table: "messages",
                column: "device_id");
        }
    }
}
