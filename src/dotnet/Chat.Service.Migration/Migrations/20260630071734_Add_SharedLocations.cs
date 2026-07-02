using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Add_SharedLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "location_id",
                table: "chat_entries",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shared_locations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    chat_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    author_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    accuracy = table.Column<float>(type: "real", nullable: true),
                    bearing = table.Column<float>(type: "real", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    stopped_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shared_locations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shared_locations_chat_id",
                table: "shared_locations",
                column: "chat_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shared_locations");

            migrationBuilder.DropColumn(
                name: "location_id",
                table: "chat_entries");
        }
    }
}
