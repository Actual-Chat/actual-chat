using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations;

/// <inheritdoc />
public partial class _20260911162134_Add_Passkeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "passkeys",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                user_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                user_handle = table.Column<byte[]>(type: "bytea", nullable: false),
                public_key = table.Column<byte[]>(type: "bytea", nullable: false),
                sign_count = table.Column<long>(type: "bigint", nullable: false),
                aaguid = table.Column<Guid>(type: "uuid", nullable: false),
                transports = table.Column<string>(type: "text", nullable: false),
                is_backup_eligible = table.Column<bool>(type: "boolean", nullable: false),
                is_backed_up = table.Column<bool>(type: "boolean", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_passkeys", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_passkeys_user_id",
            table: "passkeys",
            column: "user_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "passkeys");
    }
}
