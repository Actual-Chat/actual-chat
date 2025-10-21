using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Flows.Migrations
{
    /// <inheritdoc />
    public partial class MissingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
#if false // Original code, replaced w/ the new one below, coz this migration runs out of order on AY's machine
            migrationBuilder.DropIndex(
                name: "ix_events_delay_until",
                table: "_events");

            migrationBuilder.CreateIndex(
                name: "ix_events_delay_until_state",
                table: "_events",
                columns: new[] { "delay_until", "state" });
#endif

            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ix_events_delay_until;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_events_delay_until_state ON _events (delay_until, state);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
#if false // Original code, replaced w/ the new one below, coz this migration runs out of order on AY's machine
            migrationBuilder.DropIndex(
                name: "ix_events_delay_until_state",
                table: "_events");

            migrationBuilder.CreateIndex(
                name: "ix_events_delay_until",
                table: "_events",
                column: "delay_until");
#endif

            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ix_events_delay_until_state;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_events_delay_until ON _events (delay_until);
            ");
        }
    }
}
