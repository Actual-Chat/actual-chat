using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Flows.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "_events",
                columns: table => new
                {
                    uuid = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    logged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    delay_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    value_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.uuid);
                });

            migrationBuilder.CreateTable(
                name: "_flows",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    data_version = table.Column<int>(type: "integer", nullable: false),
                    is_completed = table.Column<bool>(type: "boolean", nullable: false),
                    result_data = table.Column<byte[]>(type: "bytea", nullable: true),
                    data = table.Column<byte[]>(type: "bytea", nullable: true),
                    console = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flows", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "_operations",
                columns: table => new
                {
                    index = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    uuid = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    host_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    logged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    command_json = table.Column<string>(type: "text", nullable: false),
                    items_json = table.Column<string>(type: "text", nullable: true),
                    nested_operations = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => x.index);
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_delay_until_state_non_new",
                table: "_events",
                columns: new[] { "delay_until", "state" },
                filter: "state != 0");

            migrationBuilder.CreateIndex(
                name: "ix_events_pending",
                table: "_events",
                column: "delay_until",
                filter: "state = 0");

            migrationBuilder.CreateIndex(
                name: "ix_flows_is_completed_version",
                table: "_flows",
                columns: new[] { "is_completed", "version" });

            migrationBuilder.CreateIndex(
                name: "ix_flows_version_is_completed",
                table: "_flows",
                columns: new[] { "version", "is_completed" });

            migrationBuilder.CreateIndex(
                name: "ix_operations_logged_at",
                table: "_operations",
                column: "logged_at");

            migrationBuilder.CreateIndex(
                name: "ix_operations_uuid",
                table: "_operations",
                column: "uuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_events");

            migrationBuilder.DropTable(
                name: "_flows");

            migrationBuilder.DropTable(
                name: "_operations");
        }
    }
}
