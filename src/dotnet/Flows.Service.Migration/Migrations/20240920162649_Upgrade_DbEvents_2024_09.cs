using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Flows.Migrations
{
    /// <inheritdoc />
    public partial class Upgrade_DbEvents_2024_09 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_flows_hard_resume_at",
                table: "_flows");

            migrationBuilder.CreateIndex(
                name: "ix_flows_hard_resume_at_step",
                table: "_flows",
                columns: new[] { "hard_resume_at", "step" });

            migrationBuilder.CreateIndex(
                name: "ix_flows_step_hard_resume_at",
                table: "_flows",
                columns: new[] { "step", "hard_resume_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_flows_hard_resume_at_step",
                table: "_flows");

            migrationBuilder.DropIndex(
                name: "ix_flows_step_hard_resume_at",
                table: "_flows");

            migrationBuilder.CreateIndex(
                name: "ix_flows_hard_resume_at",
                table: "_flows",
                column: "hard_resume_at");
        }
    }
}
