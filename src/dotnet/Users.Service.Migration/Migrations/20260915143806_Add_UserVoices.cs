using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations;

/// <inheritdoc />
public partial class _20260915143806_Add_UserVoices : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_voices",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                version = table.Column<long>(type: "bigint", nullable: false),
                sample_hash = table.Column<string>(type: "text", nullable: false),
                soniox_voice_id = table.Column<string>(type: "text", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
                failed_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_voices", x => x.id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "user_voices");
    }
}
