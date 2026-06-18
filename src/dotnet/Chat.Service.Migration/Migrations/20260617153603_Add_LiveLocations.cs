using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Add_LiveLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "live_locations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    author_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    accuracy = table.Column<float>(type: "real", nullable: true),
                    bearing = table.Column<float>(type: "real", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_locations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_live_locations_chat_id",
                table: "live_locations",
                column: "chat_id");

            migrationBuilder.CreateIndex(
                name: "IX_live_locations_expires_at",
                table: "live_locations",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_locations");
        }
    }
}
