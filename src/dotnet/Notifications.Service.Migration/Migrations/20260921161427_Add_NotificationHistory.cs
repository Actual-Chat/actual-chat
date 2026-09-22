using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notifications.Migrations;

/// <inheritdoc />
public partial class _20260921161427_Add_NotificationHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "notification_history",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                seq = table.Column<long>(type: "bigint", nullable: false),
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                kind = table.Column<int>(type: "integer", nullable: false),
                chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                entry_lid = table.Column<long>(type: "bigint", nullable: false),
                author_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                title = table.Column<string>(type: "text", nullable: false),
                text = table.Column<string>(type: "text", nullable: false),
                sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_notification_history", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_notification_history_created_at",
            table: "notification_history",
            column: "created_at");

        migrationBuilder.CreateIndex(
            name: "ix_notification_history_user_id_kind_seq",
            table: "notification_history",
            columns: new[] { "user_id", "kind", "seq" });

        migrationBuilder.CreateIndex(
            name: "ix_notification_history_user_id_seq",
            table: "notification_history",
            columns: new[] { "user_id", "seq" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "notification_history");
    }
}
