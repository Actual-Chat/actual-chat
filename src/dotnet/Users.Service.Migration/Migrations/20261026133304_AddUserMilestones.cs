using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Users.Migrations;

/// <inheritdoc />
public partial class _20261026133304_AddUserMilestones : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_milestones",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                kind = table.Column<int>(type: "integer", nullable: false),
                attribution_id = table.Column<long>(type: "bigint", nullable: true),
                reached_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_milestones", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_user_milestones_user_id_kind",
            table: "user_milestones",
            columns: new[] { "user_id", "kind" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "user_milestones");
    }
}
