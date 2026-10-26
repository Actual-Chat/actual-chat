using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Users.Migrations;

/// <inheritdoc />
public partial class _20261026134120_AddUserMilestoneDeliveries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_analytics_clients",
            columns: table => new
            {
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                kind = table.Column<int>(type: "integer", nullable: false),
                client_id = table.Column<string>(type: "text", nullable: false),
                last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_analytics_clients", x => new { x.user_id, x.kind });
            });

        migrationBuilder.CreateTable(
            name: "user_milestone_deliveries",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                milestone_id = table.Column<long>(type: "bigint", nullable: false),
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                target = table.Column<int>(type: "integer", nullable: false),
                state = table.Column<int>(type: "integer", nullable: false),
                attribution_id = table.Column<long>(type: "bigint", nullable: true),
                attempts = table.Column<int>(type: "integer", nullable: false),
                note = table.Column<string>(type: "text", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_milestone_deliveries", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_user_milestone_deliveries_milestone_id_target",
            table: "user_milestone_deliveries",
            columns: new[] { "milestone_id", "target" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_user_milestone_deliveries_user_id",
            table: "user_milestone_deliveries",
            column: "user_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "user_analytics_clients");

        migrationBuilder.DropTable(
            name: "user_milestone_deliveries");
    }
}
