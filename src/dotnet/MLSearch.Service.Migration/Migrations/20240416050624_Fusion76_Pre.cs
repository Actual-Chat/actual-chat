using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.MLSearch.Migrations
{
    /// <inheritdoc />
    public partial class Fusion76_Pre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_events");

            migrationBuilder.DropTable(
                name: "_timers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "_events",
                columns: table => new
                {
                    index = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    logged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    uuid = table.Column<string>(type: "text", nullable: false),
                    value_json = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.index);
                });

            migrationBuilder.CreateTable(
                name: "_timers",
                columns: table => new
                {
                    uuid = table.Column<string>(type: "text", nullable: false),
                    fires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    value_json = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_timers", x => x.uuid);
                });

            migrationBuilder.CreateIndex(
                name: "ix_events_logged_at",
                table: "_events",
                column: "logged_at");

            migrationBuilder.CreateIndex(
                name: "ix_events_state_logged_at",
                table: "_events",
                columns: new[] { "state", "logged_at" });

            migrationBuilder.CreateIndex(
                name: "ix_events_uuid",
                table: "_events",
                column: "uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_timers_fires_at",
                table: "_timers",
                column: "fires_at");

            migrationBuilder.CreateIndex(
                name: "ix_timers_state_fires_at",
                table: "_timers",
                columns: new[] { "state", "fires_at" });
        }
    }
}
