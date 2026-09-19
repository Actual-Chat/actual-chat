using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations;

/// <inheritdoc />
public partial class _20260918013314_AddMaintenanceOwner : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "owner_id",
            table: "maintenances",
            type: "text",
            nullable: false,
            defaultValue: "");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "owner_id",
            table: "maintenances");
    }
}
