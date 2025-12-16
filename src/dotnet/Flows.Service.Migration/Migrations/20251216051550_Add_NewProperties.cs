using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Flows.Migrations
{
    /// <inheritdoc />
    public partial class Add_NewProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_flows_hard_resume_at_step",
                table: "_flows");

            migrationBuilder.DropIndex(
                name: "ix_flows_step_hard_resume_at",
                table: "_flows");

            migrationBuilder.DropColumn(
                name: "hard_resume_at",
                table: "_flows");

            migrationBuilder.DropColumn(
                name: "step",
                table: "_flows");

            migrationBuilder.AddColumn<int>(
                name: "data_version",
                table: "_flows",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "data_version",
                table: "_flows");

            migrationBuilder.AddColumn<DateTime>(
                name: "hard_resume_at",
                table: "_flows",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "step",
                table: "_flows",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                defaultValue: "",
                collation: "C");

            migrationBuilder.CreateIndex(
                name: "ix_flows_hard_resume_at_step",
                table: "_flows",
                columns: new[] { "hard_resume_at", "step" });

            migrationBuilder.CreateIndex(
                name: "ix_flows_step_hard_resume_at",
                table: "_flows",
                columns: new[] { "step", "hard_resume_at" });
        }
    }
}
