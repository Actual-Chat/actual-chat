using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations;

/// <inheritdoc />
public partial class _20260929122126_Coach_DayLanguage : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Day rows are derived from coach_events; Coach_RebuildOwnDays refills them per language
        migrationBuilder.Sql("DELETE FROM coach_days;");

        migrationBuilder.DropPrimaryKey(
            name: "PK_coach_days",
            table: "coach_days");

        migrationBuilder.AddColumn<string>(
            name: "language",
            table: "coach_days",
            type: "text",
            nullable: false,
            defaultValue: "",
            collation: "C");

        migrationBuilder.AddPrimaryKey(
            name: "PK_coach_days",
            table: "coach_days",
            columns: new[] { "user_id", "day", "language" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "PK_coach_days",
            table: "coach_days");

        migrationBuilder.DropColumn(
            name: "language",
            table: "coach_days");

        migrationBuilder.AddPrimaryKey(
            name: "PK_coach_days",
            table: "coach_days",
            columns: new[] { "user_id", "day" });
    }
}
