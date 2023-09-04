using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class AddIsGreetingCompleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_greeting_completed",
                table: "accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_greeting_completed",
                table: "accounts");
        }
    }
}
