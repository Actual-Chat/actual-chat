using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Media.Migrations;

/// <inheritdoc />
public partial class _20260916085525_AddImageSuggestions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "image_suggestions",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                version = table.Column<long>(type: "bigint", nullable: false),
                media_id = table.Column<string>(type: "text", nullable: true, collation: "C"),
                image_description = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                dismissed_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_image_suggestions", x => x.id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "image_suggestions");
    }
}
