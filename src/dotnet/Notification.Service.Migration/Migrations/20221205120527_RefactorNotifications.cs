using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notification.Migrations
{
    /// <inheritdoc />
    public partial class RefactorNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "muted_chat_subscriptions");

            migrationBuilder.DropColumn(
                name: "modified_at",
                table: "notifications");

            migrationBuilder.RenameColumn(
                name: "notification_type",
                table: "notifications",
                newName: "kind");

            migrationBuilder.RenameColumn(
                name: "chat_entry_id",
                table: "notifications",
                newName: "text_entry_local_id");

            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "notifications",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                table: "notifications");

            migrationBuilder.RenameColumn(
                name: "text_entry_local_id",
                table: "notifications",
                newName: "chat_entry_id");

            migrationBuilder.RenameColumn(
                name: "kind",
                table: "notifications",
                newName: "notification_type");

            migrationBuilder.AddColumn<DateTime>(
                name: "modified_at",
                table: "notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "muted_chat_subscriptions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    chatid = table.Column<string>(name: "chat_id", type: "text", nullable: false),
                    userid = table.Column<string>(name: "user_id", type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_muted_chat_subscriptions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_muted_chat_subscriptions_user_id_chat_id",
                table: "muted_chat_subscriptions",
                columns: new[] { "user_id", "chat_id" });
        }
    }
}
