using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notifications.Migrations;

/// <inheritdoc />
public partial class _20260819114716_DropUnsentDeltaFlag : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "has_unsent_delta",
            table: "user_notifications");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "has_unsent_delta",
            table: "user_notifications",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }
}
