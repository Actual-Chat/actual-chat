using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations;

/// <inheritdoc />
public partial class _20260928101231_Add_Coach : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "coach_days",
            columns: table => new
            {
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                day = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
                data = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_coach_days", x => new { x.user_id, x.day });
            });

        migrationBuilder.CreateTable(
            name: "coach_events",
            columns: table => new
            {
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                source_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                kind = table.Column<int>(type: "integer", nullable: false),
                chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                day = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_coach_events", x => new { x.user_id, x.source_id });
            });

        migrationBuilder.CreateIndex(
            name: "ix_coach_events_user_id_day",
            table: "coach_events",
            columns: new[] { "user_id", "day" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "coach_days");

        migrationBuilder.DropTable(
            name: "coach_events");
    }
}
