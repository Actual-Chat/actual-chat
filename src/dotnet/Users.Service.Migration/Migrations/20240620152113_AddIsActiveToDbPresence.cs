using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class AddIsActiveToDbPresence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "online_check_in_at",
                table: "presences",
                newName: "check_in_at");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "presences",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_active",
                table: "presences");

            migrationBuilder.RenameColumn(
                name: "check_in_at",
                table: "presences",
                newName: "online_check_in_at");
        }
    }
}
