using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Flows.Migrations
{
    /// <inheritdoc />
    public partial class Update : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_events_uuid",
                table: "_events");

            migrationBuilder.AddColumn<bool>(
                name: "can_resume",
                table: "_flows",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_flows_can_resume",
                table: "_flows",
                column: "can_resume");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_flows_can_resume",
                table: "_flows");

            migrationBuilder.DropColumn(
                name: "can_resume",
                table: "_flows");

            migrationBuilder.CreateIndex(
                name: "ix_events_uuid",
                table: "_events",
                column: "uuid",
                unique: true);
        }
    }
}
