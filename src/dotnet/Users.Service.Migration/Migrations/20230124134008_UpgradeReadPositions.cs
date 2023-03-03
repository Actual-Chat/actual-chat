using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeReadPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                update read_positions
                set id = concat(id, ':0');
                """);

            migrationBuilder.RenameColumn(
                name: "read_entry_id",
                table: "read_positions",
                newName: "entry_lid");

            migrationBuilder.AddColumn<int>(
                name: "kind",
                table: "read_positions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "read_positions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                table: "read_positions");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "read_positions");

            migrationBuilder.RenameColumn(
                name: "entry_lid",
                table: "read_positions",
                newName: "read_entry_id");

            migrationBuilder.Sql("""
                update read_positions
                set id = substring(id for length(id) - 2);
                """);
        }
    }
}
