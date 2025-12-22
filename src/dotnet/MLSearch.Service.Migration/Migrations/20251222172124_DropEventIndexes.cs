using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.MLSearch.Migrations
{
    /// <inheritdoc />
    public partial class DropEventIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_events_delay_until_state",
                table: "_events");

            migrationBuilder.DropIndex(
                name: "ix_events_state_delay_until",
                table: "_events");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_events_delay_until_state",
                table: "_events",
                columns: new[] { "delay_until", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_events_state_delay_until",
                table: "_events",
                columns: new[] { "state", "delay_until" });
        }
    }
}
