using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Flows.Migrations
{
    /// <inheritdoc />
    public partial class Update2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_flows_can_resume",
                table: "_flows");

            migrationBuilder.DropColumn(
                name: "can_resume",
                table: "_flows");

            migrationBuilder.AddColumn<DateTime>(
                name: "hard_resume_at",
                table: "_flows",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_flows_hard_resume_at",
                table: "_flows",
                column: "hard_resume_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_flows_hard_resume_at",
                table: "_flows");

            migrationBuilder.DropColumn(
                name: "hard_resume_at",
                table: "_flows");

            migrationBuilder.AddColumn<bool>(
                name: "can_resume",
                table: "_flows",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_flows_can_resume",
                table: "_flows",
                column: "can_resume");
        }
    }
}
