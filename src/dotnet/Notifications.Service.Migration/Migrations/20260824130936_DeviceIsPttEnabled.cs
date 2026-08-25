using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notifications.Migrations;

/// <inheritdoc />
public partial class _20260824130936_DeviceIsPttEnabled : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "is_ptt_enabled",
            table: "devices",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "is_ptt_enabled",
            table: "devices");
    }
}
