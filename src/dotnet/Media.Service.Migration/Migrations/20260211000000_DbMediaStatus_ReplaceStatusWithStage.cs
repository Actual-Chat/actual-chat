using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Media.Migrations
{
    /// <inheritdoc />
    public partial class DbMediaStatus_ReplaceStatusWithStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "status",
                table: "media_statuses");

            migrationBuilder.DropColumn(
                name: "preparing_stage",
                table: "media_statuses");

            migrationBuilder.AddColumn<int>(
                name: "stage",
                table: "media_statuses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "error_message",
                table: "media_statuses",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "stage",
                table: "media_statuses");

            migrationBuilder.DropColumn(
                name: "error_message",
                table: "media_statuses");

            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "media_statuses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "preparing_stage",
                table: "media_statuses",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
