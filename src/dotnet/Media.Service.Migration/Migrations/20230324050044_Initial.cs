using System;
using Microsoft.EntityFrameworkCore.Migrations;

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
                name: "_operations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    agentid = table.Column<string>(name: "agent_id", type: "text", nullable: false),
                    starttime = table.Column<DateTime>(name: "start_time", type: "timestamp with time zone", nullable: false),
                    committime = table.Column<DateTime>(name: "commit_time", type: "timestamp with time zone", nullable: false),
                    commandjson = table.Column<string>(name: "command_json", type: "text", nullable: false),
                    itemsjson = table.Column<string>(name: "items_json", type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "media",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false),
                    localid = table.Column<string>(name: "local_id", type: "text", nullable: false),
                    contentid = table.Column<string>(name: "content_id", type: "text", nullable: false),
                    metadatajson = table.Column<string>(name: "metadata_json", type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_commit_time",
                table: "_operations",
                column: "commit_time");

            migrationBuilder.CreateIndex(
                name: "ix_start_time",
                table: "_operations",
                column: "start_time");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_operations");

            migrationBuilder.DropTable(
                name: "media");
        }
    }
}
