using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Flows.Migrations
{
    /// <inheritdoc />
    public partial class Add_Result : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Removed due to merge with the previous migration (MissingChanges)
            // migrationBuilder.DropIndex(
            //     name: "ix_events_delay_until",
            //     table: "_events");

            migrationBuilder.AddColumn<bool>(
                name: "is_completed",
                table: "_flows",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "result_data",
                table: "_flows",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_flows_is_completed_version",
                table: "_flows",
                columns: new[] { "is_completed", "version" });

            migrationBuilder.CreateIndex(
                name: "ix_flows_version_is_completed",
                table: "_flows",
                columns: new[] { "version", "is_completed" });

            // Removed due to merge with the previous migration (MissingChanges)
            // migrationBuilder.CreateIndex(
            //     name: "ix_events_delay_until_state",
            //     table: "_events",
            //     columns: new[] { "delay_until", "state" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_flows_is_completed_version",
                table: "_flows");

            migrationBuilder.DropIndex(
                name: "ix_flows_version_is_completed",
                table: "_flows");

            // Removed due to merge with the previous migration (MissingChanges)
            // migrationBuilder.DropIndex(
            //     name: "ix_events_delay_until_state",
            //     table: "_events");

            migrationBuilder.DropColumn(
                name: "is_completed",
                table: "_flows");

            migrationBuilder.DropColumn(
                name: "result_data",
                table: "_flows");

            // Removed due to merge with the previous migration (MissingChanges)
            // migrationBuilder.CreateIndex(
            //     name: "ix_events_delay_until",
            //     table: "_events",
            //     column: "delay_until");
        }
    }
}
