using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations;

/// <inheritdoc />
public partial class _20260921075246_AddUsage : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "usage_days",
            columns: table => new
            {
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                day = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
                speech_ms = table.Column<long>(type: "bigint", nullable: false),
                speech_entries = table.Column<int>(type: "integer", nullable: false),
                messages = table.Column<int>(type: "integer", nullable: false),
                live_sessions = table.Column<int>(type: "integer", nullable: false),
                contacts_added = table.Column<int>(type: "integer", nullable: false),
                contacts_removed = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_usage_days", x => new { x.user_id, x.day });
            });

        migrationBuilder.CreateTable(
            name: "usage_events",
            columns: table => new
            {
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                kind = table.Column<int>(type: "integer", nullable: false),
                source_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                value = table.Column<long>(type: "bigint", nullable: false),
                attributes = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_usage_events", x => new { x.user_id, x.occurred_at, x.kind, x.source_id });
            });

        migrationBuilder.CreateIndex(
            name: "ix_usage_events_user_id_kind_source_id",
            table: "usage_events",
            columns: new[] { "user_id", "kind", "source_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "usage_days");

        migrationBuilder.DropTable(
            name: "usage_events");
    }
}
