using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Media.Migrations
{
    /// <inheritdoc />
    public partial class Rename_DbMediaProgress_ErrorMessage_To_Error : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "error_message",
                table: "media_progresses",
                newName: "error");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "error",
                table: "media_progresses",
                newName: "error_message");
        }
    }
}
