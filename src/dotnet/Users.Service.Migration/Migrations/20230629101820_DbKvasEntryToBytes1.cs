using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class DbKvasEntryToBytes1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "new_value",
                table: "kvas_entries",
                type: "bytea",
                nullable: false,
                defaultValue: Array.Empty<byte>());

            migrationBuilder.Sql("""
                update kvas_entries
                set new_value = convert_to(value, 'utf-8');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "new_value",
                table: "kvas_entries");
        }
    }
}
