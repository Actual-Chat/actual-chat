using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "_operations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    agent_id = table.Column<string>(type: "text", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    commit_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    command_json = table.Column<string>(type: "text", nullable: false),
                    items_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ipaddress = table.Column<string>(type: "text", nullable: false),
                    user_agent = table.Column<string>(type: "text", nullable: false),
                    authenticated_identity = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    is_sign_out_forced = table.Column<bool>(type: "boolean", nullable: false),
                    options_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_read_positions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    read_entry_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_read_positions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_user_settings",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    chat_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    avatar_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_user_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_avatars",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    local_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    picture = table.Column<string>(type: "text", nullable: false),
                    bio = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_avatars", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_contacts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    owner_user_id = table.Column<string>(type: "text", nullable: false),
                    target_user_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_presences",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    online_check_in_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_presences", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "user_profiles",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    avatar_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_profiles", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
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
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
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
                name: "ix_commit_time",
                table: "_operations",
                column: "commit_time");

            migrationBuilder.CreateIndex(
                name: "ix_start_time",
                table: "_operations",
                column: "start_time");

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
                name: "ix_chat_user_settings_chat_id_user_id",
                table: "chat_user_settings",
                columns: new[] { "chat_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_contacts_owner_user_id",
                table: "user_contacts",
                column: "owner_user_id");

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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_operations");

            migrationBuilder.DropTable(
                name: "_sessions");

            migrationBuilder.DropTable(
                name: "chat_read_positions");

            migrationBuilder.DropTable(
                name: "chat_user_settings");

            migrationBuilder.DropTable(
                name: "user_avatars");

            migrationBuilder.DropTable(
                name: "user_contacts");

            migrationBuilder.DropTable(
                name: "user_identities");

            migrationBuilder.DropTable(
                name: "user_presences");

            migrationBuilder.DropTable(
                name: "user_profiles");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
