using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notification.Migrations
{
    /// <inheritdoc />
    public partial class AddManualNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "manual_notifications",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    similarity_key = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manual_notifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_manual_notifications_user_id_id",
                table: "manual_notifications",
                columns: new[] { "user_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_manual_notifications_user_id_kind_similarity_key",
                table: "manual_notifications",
                columns: new[] { "user_id", "kind", "similarity_key" });

            migrationBuilder.CreateIndex(
                name: "ix_manual_notifications_user_id_version",
                table: "manual_notifications",
                columns: new[] { "user_id", "version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "manual_notifications");
        }
    }
}
