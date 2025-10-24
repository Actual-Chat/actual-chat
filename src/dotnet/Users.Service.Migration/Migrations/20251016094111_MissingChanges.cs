using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class MissingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_events_delay_until",
                table: "_events");

            migrationBuilder.CreateIndex(
                name: "ix_events_delay_until_state",
                table: "_events",
                columns: new[] { "delay_until", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_events_delay_until_state",
                table: "_events");

            migrationBuilder.CreateIndex(
                name: "ix_events_delay_until",
                table: "_events",
                column: "delay_until");
        }
    }
}
