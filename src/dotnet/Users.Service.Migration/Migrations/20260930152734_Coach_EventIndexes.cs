using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations;

/// <inheritdoc />
public partial class _20260930152734_Coach_EventIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_coach_events_user_id_chat_id_occurred_at",
            table: "coach_events",
            columns: new[] { "user_id", "chat_id", "occurred_at" });

        migrationBuilder.CreateIndex(
            name: "ix_coach_events_user_id_occurred_at",
            table: "coach_events",
            columns: new[] { "user_id", "occurred_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_coach_events_user_id_chat_id_occurred_at",
            table: "coach_events");

        migrationBuilder.DropIndex(
            name: "ix_coach_events_user_id_occurred_at",
            table: "coach_events");
    }
}
