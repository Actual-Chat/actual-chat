using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Invite.Migrations
{
    /// <inheritdoc />
    public partial class AddEventIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_events_delay_until_state_non_new",
                table: "_events",
                columns: new[] { "delay_until", "state" },
                filter: "state != 0");

            migrationBuilder.CreateIndex(
                name: "ix_events_pending",
                table: "_events",
                column: "delay_until",
                filter: "state = 0")
                .Annotation("Npgsql:IndexInclude", new[] { "uuid", "version", "logged_at", "value_json", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_events_delay_until_state_non_new",
                table: "_events");

            migrationBuilder.DropIndex(
                name: "ix_events_pending",
                table: "_events");
        }
    }
}
