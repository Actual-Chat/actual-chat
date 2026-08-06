using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Media.Migrations
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
                name: "grab_statuses",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_grab_statuses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "link_previews",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    thumbnail_media_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    title = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    metadata_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_link_previews", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "media",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    scope = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    local_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    content_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    metadata_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media", x => x.id);
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
                name: "ix_media_content_id",
                table: "media",
                column: "content_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_events");

            migrationBuilder.DropTable(
                name: "_operations");

            migrationBuilder.DropTable(
                name: "grab_statuses");

            migrationBuilder.DropTable(
                name: "link_previews");

            migrationBuilder.DropTable(
                name: "media");
        }
    }
}
