using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations;

/// <inheritdoc />
public partial class _20260917163140_Add_WebHooks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "web_hook_deliveries",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                web_hook_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                seq = table.Column<long>(type: "bigint", nullable: false),
                event_type = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_status_code = table.Column<int>(type: "integer", nullable: true),
                last_error = table.Column<string>(type: "text", nullable: true),
                last_latency_ms = table.Column<int>(type: "integer", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                payload = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_web_hook_deliveries", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "web_hooks",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                version = table.Column<long>(type: "bigint", nullable: false),
                scope = table.Column<int>(type: "integer", nullable: false),
                scope_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                kind = table.Column<int>(type: "integer", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                created_by = table.Column<string>(type: "text", nullable: true, collation: "C"),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                disabled_reason = table.Column<int>(type: "integer", nullable: false),
                last_activity_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                url = table.Column<string>(type: "text", nullable: false),
                events = table.Column<long>(type: "bigint", nullable: false),
                include_text = table.Column<bool>(type: "boolean", nullable: false),
                chat_ids = table.Column<string>(type: "text", nullable: false),
                subscribe_notifications = table.Column<bool>(type: "boolean", nullable: false),
                custom_header_name = table.Column<string>(type: "text", nullable: true),
                consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                last_status_code = table.Column<int>(type: "integer", nullable: true),
                last_error = table.Column<string>(type: "text", nullable: true),
                secret_protected = table.Column<string>(type: "text", nullable: true),
                prev_secret_protected = table.Column<string>(type: "text", nullable: true),
                prev_secret_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                custom_header_value_protected = table.Column<string>(type: "text", nullable: true),
                token_hash = table.Column<string>(type: "text", nullable: true, collation: "C")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_web_hooks", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_web_hook_deliveries_created_at",
            table: "web_hook_deliveries",
            column: "created_at");

        migrationBuilder.CreateIndex(
            name: "ix_web_hook_deliveries_status_next_attempt_at",
            table: "web_hook_deliveries",
            columns: new[] { "status", "next_attempt_at" });

        migrationBuilder.CreateIndex(
            name: "ix_web_hook_deliveries_web_hook_id_seq",
            table: "web_hook_deliveries",
            columns: new[] { "web_hook_id", "seq" });

        migrationBuilder.CreateIndex(
            name: "ix_web_hooks_scope_id",
            table: "web_hooks",
            column: "scope_id");

        migrationBuilder.CreateIndex(
            name: "ix_web_hooks_token_hash",
            table: "web_hooks",
            column: "token_hash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "web_hook_deliveries");

        migrationBuilder.DropTable(
            name: "web_hooks");
    }
}
