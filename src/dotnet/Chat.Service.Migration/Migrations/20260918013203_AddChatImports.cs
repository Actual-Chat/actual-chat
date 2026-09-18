using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations;

/// <inheritdoc />
public partial class _20260918013203_AddChatImports : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "is_imported",
            table: "chat_entries",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "chat_import_batches",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                request = table.Column<string>(type: "text", nullable: false),
                result = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_chat_import_batches", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "chat_import_consents",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                import_id = table.Column<string>(type: "text", nullable: false),
                user_id = table.Column<string>(type: "text", nullable: false),
                has_consent = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_chat_import_consents", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "chat_import_uploads",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                chat_id = table.Column<string>(type: "text", nullable: false),
                import_id = table.Column<string>(type: "text", nullable: false),
                user_id = table.Column<string>(type: "text", nullable: false),
                uploaded_by = table.Column<string>(type: "text", nullable: false),
                media_json = table.Column<string>(type: "text", nullable: false),
                entry_id = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_chat_import_uploads", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "chat_imports",
            columns: table => new
            {
                id = table.Column<string>(type: "text", nullable: false),
                import_id = table.Column<string>(type: "text", nullable: false),
                started_by = table.Column<string>(type: "text", nullable: false),
                started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_chat_imports", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_chat_import_consents_import_id",
            table: "chat_import_consents",
            column: "import_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "chat_import_batches");

        migrationBuilder.DropTable(
            name: "chat_import_consents");

        migrationBuilder.DropTable(
            name: "chat_import_uploads");

        migrationBuilder.DropTable(
            name: "chat_imports");

        migrationBuilder.DropColumn(
            name: "is_imported",
            table: "chat_entries");
    }
}
