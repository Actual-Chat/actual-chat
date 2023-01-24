using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class RenameReadPositions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_read_positions",
                table: "read_positions");

            migrationBuilder.RenameTable(
                name: "read_positions",
                newName: "chat_positions");

            migrationBuilder.AddPrimaryKey(
                name: "pk_chat_positions",
                table: "chat_positions",
                column: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_chat_positions",
                table: "chat_positions");

            migrationBuilder.RenameTable(
                name: "chat_positions",
                newName: "read_positions");

            migrationBuilder.AddPrimaryKey(
                name: "pk_read_positions",
                table: "read_positions",
                column: "id");
        }
    }
}
