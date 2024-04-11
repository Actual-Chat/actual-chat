using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.MLSearch.Migrations
{
    /// <inheritdoc />
    public partial class Fusion75_Pre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop table if exists _operations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "_operations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    agent_id = table.Column<string>(type: "text", nullable: false),
                    command_json = table.Column<string>(type: "text", nullable: false),
                    commit_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    items_json = table.Column<string>(type: "text", nullable: false),
                    start_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => x.id);
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
    }
}
