using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations;

/// <inheritdoc />
public partial class _20261016100322_Add_JoinRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "join_requests",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                version = table.Column<long>(type: "bigint", nullable: false),
                chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                status = table.Column<int>(type: "integer", nullable: false),
                parent_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                invite_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                was_excluded = table.Column<bool>(type: "boolean", nullable: false),
                decided_by = table.Column<string>(type: "text", nullable: true, collation: "C"),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_join_requests", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_join_requests_chat_id_status",
            table: "join_requests",
            columns: new[] { "chat_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_join_requests_chat_id_user_id_open",
            table: "join_requests",
            columns: new[] { "chat_id", "user_id" },
            unique: true,
            filter: "\"status\" IN (0, 5)");

        migrationBuilder.CreateIndex(
            name: "ix_join_requests_parent_id",
            table: "join_requests",
            column: "parent_id");

        migrationBuilder.CreateIndex(
            name: "ix_join_requests_status_expires_at",
            table: "join_requests",
            columns: new[] { "status", "expires_at" });

        migrationBuilder.CreateIndex(
            name: "ix_join_requests_user_id_chat_id",
            table: "join_requests",
            columns: new[] { "user_id", "chat_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "join_requests");
    }
}
