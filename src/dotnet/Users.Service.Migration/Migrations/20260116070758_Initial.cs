using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Users.Migrations
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
                name: "_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ipaddress = table.Column<string>(type: "text", nullable: false),
                    user_agent = table.Column<string>(type: "text", nullable: false),
                    authenticated_identity = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    is_sign_out_forced = table.Column<bool>(type: "boolean", nullable: false),
                    options_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    is_email_verified = table.Column<bool>(type: "boolean", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: false),
                    sync_contacts = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    username = table.Column<string>(type: "text", nullable: false),
                    username_normalized = table.Column<string>(type: "text", nullable: true),
                    is_greeting_completed = table.Column<bool>(type: "boolean", nullable: false),
                    time_zone = table.Column<string>(type: "text", nullable: false),
                    alias_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "avatars",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                    name = table.Column<string>(type: "text", nullable: false),
                    picture = table.Column<string>(type: "text", nullable: false),
                    media_id = table.Column<string>(type: "text", nullable: false),
                    avatar_key = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    bio = table.Column<string>(type: "text", nullable: false),
                    is_anonymous = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avatars", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_positions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    entry_lid = table.Column<long>(type: "bigint", nullable: false),
                    origin = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_positions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_usages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    accessed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_usages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kvas_entries",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kvas_entries", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "presences",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    check_in_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_presences", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "roulette_completions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    owner_user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    owner_profile_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    peer_user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    peer_profile_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    complete_reason = table.Column<int>(type: "integer", nullable: false),
                    is_initiated_by_owner = table.Column<bool>(type: "boolean", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roulette_completions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roulette_profile_prefs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    country = table.Column<string>(type: "text", nullable: false),
                    gender = table.Column<int>(type: "integer", nullable: false),
                    languages = table.Column<string>(type: "text", nullable: false),
                    interests = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roulette_profile_prefs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roulette_user_settings",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roulette_user_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    claims_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_identities",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    secret = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_identities", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_identities_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
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
                name: "ix_sessions_created_at_is_sign_out_forced",
                table: "_sessions",
                columns: new[] { "created_at", "is_sign_out_forced" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_ipaddress_is_sign_out_forced",
                table: "_sessions",
                columns: new[] { "ipaddress", "is_sign_out_forced" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_last_seen_at_is_sign_out_forced",
                table: "_sessions",
                columns: new[] { "last_seen_at", "is_sign_out_forced" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user_id_is_sign_out_forced",
                table: "_sessions",
                columns: new[] { "user_id", "is_sign_out_forced" });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_alias_id",
                table: "accounts",
                column: "alias_id",
                unique: true,
                filter: "alias_id <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_id_time_zone",
                table: "accounts",
                columns: new[] { "id", "time_zone" });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_is_greeting_completed",
                table: "accounts",
                column: "is_greeting_completed");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_username_normalized",
                table: "accounts",
                column: "username_normalized",
                unique: true,
                filter: "username_normalized is not null");

            migrationBuilder.CreateIndex(
                name: "ix_accounts_version_id",
                table: "accounts",
                columns: new[] { "version", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_identities_id",
                table: "user_identities",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_user_identities_user_id",
                table: "user_identities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_name",
                table: "users",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_events");

            migrationBuilder.DropTable(
                name: "_operations");

            migrationBuilder.DropTable(
                name: "_sessions");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "avatars");

            migrationBuilder.DropTable(
                name: "chat_positions");

            migrationBuilder.DropTable(
                name: "chat_usages");

            migrationBuilder.DropTable(
                name: "kvas_entries");

            migrationBuilder.DropTable(
                name: "presences");

            migrationBuilder.DropTable(
                name: "roulette_completions");

            migrationBuilder.DropTable(
                name: "roulette_profile_prefs");

            migrationBuilder.DropTable(
                name: "roulette_user_settings");

            migrationBuilder.DropTable(
                name: "user_identities");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
