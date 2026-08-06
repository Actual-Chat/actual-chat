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
            migrationBuilder.DropTable(
                name: "roulette_completions");

            migrationBuilder.DropTable(
                name: "roulette_profile_prefs");

            migrationBuilder.DropTable(
                name: "roulette_user_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "roulette_completions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    complete_reason = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_initiated_by_owner = table.Column<bool>(type: "boolean", nullable: false),
                    owner_profile_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    owner_user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    peer_profile_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    peer_user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false)
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
                    country = table.Column<string>(type: "text", nullable: false),
                    gender = table.Column<int>(type: "integer", nullable: false),
                    interests = table.Column<string>(type: "text", nullable: false),
                    languages = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
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
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roulette_user_settings", x => x.id);
                });
        }
    }
}
