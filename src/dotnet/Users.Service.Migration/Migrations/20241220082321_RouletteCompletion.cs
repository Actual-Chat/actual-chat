using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class RouletteCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "roulette_completions");
        }
    }
}
