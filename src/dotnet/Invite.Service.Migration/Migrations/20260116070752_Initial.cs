using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Invite.Migrations
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

            migrationBuilder.CreateTable(
                name: "invite_activation_keys",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invite_activation_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invites",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    search_key = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    remaining = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    details_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invites", x => x.id);
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
                name: "ix_operations_logged_at",
                table: "_operations",
                column: "logged_at");

            migrationBuilder.CreateIndex(
                name: "ix_operations_uuid",
                table: "_operations",
                column: "uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invites_search_key_remaining",
                table: "invites",
                columns: new[] { "search_key", "remaining" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_events");

            migrationBuilder.DropTable(
                name: "_operations");

            migrationBuilder.DropTable(
                name: "invite_activation_keys");

            migrationBuilder.DropTable(
                name: "invites");
        }
    }
}
