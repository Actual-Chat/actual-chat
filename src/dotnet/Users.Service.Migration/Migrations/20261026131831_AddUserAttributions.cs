using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ActualChat.Users.Migrations;

/// <inheritdoc />
public partial class _20261026131831_AddUserAttributions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_attributions",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                origin = table.Column<int>(type: "integer", nullable: false),
                is_initial = table.Column<bool>(type: "boolean", nullable: false),
                is_latest = table.Column<bool>(type: "boolean", nullable: false),
                first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                platform = table.Column<string>(type: "text", nullable: false),
                source = table.Column<string>(type: "text", nullable: false),
                medium = table.Column<string>(type: "text", nullable: false),
                campaign = table.Column<string>(type: "text", nullable: false),
                ad_group = table.Column<string>(type: "text", nullable: false),
                term = table.Column<string>(type: "text", nullable: false),
                keyword_id = table.Column<string>(type: "text", nullable: false),
                match_type = table.Column<string>(type: "text", nullable: false),
                ad_id = table.Column<string>(type: "text", nullable: false),
                click_id_kind = table.Column<int>(type: "integer", nullable: true),
                click_id = table.Column<string>(type: "text", nullable: false),
                claim = table.Column<string>(type: "text", nullable: false),
                install_kind = table.Column<string>(type: "text", nullable: false),
                country = table.Column<string>(type: "text", nullable: false),
                raw = table.Column<string>(type: "text", nullable: false),
                dedupe_key = table.Column<string>(type: "text", nullable: false, collation: "C")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_attributions", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_user_attributions_initial",
            table: "user_attributions",
            column: "user_id",
            unique: true,
            filter: "is_initial");

        migrationBuilder.CreateIndex(
            name: "ix_user_attributions_latest",
            table: "user_attributions",
            column: "user_id",
            unique: true,
            filter: "is_latest");

        migrationBuilder.CreateIndex(
            name: "ix_user_attributions_user_id_dedupe_key",
            table: "user_attributions",
            columns: new[] { "user_id", "dedupe_key" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "user_attributions");
    }
}
