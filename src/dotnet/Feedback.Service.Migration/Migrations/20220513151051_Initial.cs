using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Feedback.Migrations
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
                name: "feature_requests",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: false),
                    feature_name = table.Column<string>(type: "text", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_requests", x => x.id);
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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_operations");

            migrationBuilder.DropTable(
                name: "feature_requests");
        }
    }
}
