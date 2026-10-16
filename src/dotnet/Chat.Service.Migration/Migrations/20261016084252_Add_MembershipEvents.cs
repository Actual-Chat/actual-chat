using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations;

/// <inheritdoc />
public partial class _20261016084252_Add_MembershipEvents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "membership_events",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                author_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                kind = table.Column<int>(type: "integer", nullable: false),
                method = table.Column<int>(type: "integer", nullable: false),
                actor_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                invite_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                request_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_membership_events", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_membership_events_chat_id_created_at",
            table: "membership_events",
            columns: new[] { "chat_id", "created_at" });

        migrationBuilder.CreateIndex(
            name: "ix_membership_events_invite_id",
            table: "membership_events",
            column: "invite_id");

        migrationBuilder.CreateIndex(
            name: "ix_membership_events_user_id",
            table: "membership_events",
            column: "user_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "membership_events");
    }
}
