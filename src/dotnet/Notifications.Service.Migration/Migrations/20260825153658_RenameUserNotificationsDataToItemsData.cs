using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Notifications.Migrations;

/// <inheritdoc />
public partial class _20260825153658_RenameUserNotificationsDataToItemsData : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "data",
            table: "user_notifications",
            newName: "items_data");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "items_data",
            table: "user_notifications",
            newName: "data");
    }
}
