using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Flows.Migrations
{
    /// <inheritdoc />
    public partial class AddDbFlowIsFailed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_failed",
                table: "_flows",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_flows_is_failed_version",
                table: "_flows",
                columns: new[] { "is_failed", "version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_flows_is_failed_version",
                table: "_flows");

            migrationBuilder.DropColumn(
                name: "is_failed",
                table: "_flows");
        }
    }
}
