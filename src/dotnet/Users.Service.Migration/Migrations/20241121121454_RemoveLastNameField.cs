using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLastNameField : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                update accounts
                set name = concat(name, ' ', last_name)
                where last_name <> '';
                """);

            migrationBuilder.DropColumn(
                name: "last_name",
                table: "accounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "last_name",
                table: "accounts",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
