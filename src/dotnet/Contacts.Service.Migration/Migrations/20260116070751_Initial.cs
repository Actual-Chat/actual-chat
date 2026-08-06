using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Contacts.Migrations
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
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    user_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    chat_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    place_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    peer_contact_name = table.Column<string>(type: "text", nullable: false),
                    external_contact_name = table.Column<string>(type: "text", nullable: false),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    touched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_contacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    given_name = table.Column<string>(type: "text", nullable: false),
                    family_name = table.Column<string>(type: "text", nullable: false),
                    middle_name = table.Column<string>(type: "text", nullable: false),
                    name_prefix = table.Column<string>(type: "text", nullable: false),
                    name_suffix = table.Column<string>(type: "text", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_contacts_hashes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_contacts_hashes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "place_contacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    place_id = table.Column<string>(type: "text", nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_place_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "thread_contacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    thread_chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    parent_chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    outermost_parent_chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    place_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    touched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thread_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_contact_links",
                columns: table => new
                {
                    external_contact_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    value = table.Column<string>(type: "text", nullable: false),
                    is_checked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_contact_links", x => new { x.external_contact_id, x.value });
                    table.ForeignKey(
                        name: "fk_external_contact_links_external_contacts_external_contact_id",
                        column: x => x.external_contact_id,
                        principalTable: "external_contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "ix_contacts_owner_id",
                table: "contacts",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_version_id",
                table: "contacts",
                columns: new[] { "version", "id" },
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_external_contact_links_is_checked",
                table: "external_contact_links",
                column: "is_checked");

            migrationBuilder.CreateIndex(
                name: "ix_external_contact_links_value",
                table: "external_contact_links",
                column: "value");

            migrationBuilder.CreateIndex(
                name: "ix_place_contacts_owner_id",
                table: "place_contacts",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_place_contacts_version_id",
                table: "place_contacts",
                columns: new[] { "version", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_thread_contacts_owner_id",
                table: "thread_contacts",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_events");

            migrationBuilder.DropTable(
                name: "_operations");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "external_contact_links");

            migrationBuilder.DropTable(
                name: "external_contacts_hashes");

            migrationBuilder.DropTable(
                name: "place_contacts");

            migrationBuilder.DropTable(
                name: "thread_contacts");

            migrationBuilder.DropTable(
                name: "external_contacts");
        }
    }
}
