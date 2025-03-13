using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class ExtractChatEntryLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chat_entries_chat_id",
                table: "chat_entries");

            migrationBuilder.DropColumn(
                name: "languages",
                table: "chat_entries");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "translations",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateTable(
                name: "chat_entry_languages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    languages = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_entry_languages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_entry_languages_id",
                table: "chat_entry_languages",
                column: "id",
                filter: "languages = ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_entry_languages");

            migrationBuilder.AlterColumn<string>(
                name: "id",
                table: "translations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AddColumn<string>(
                name: "languages",
                table: "chat_entries",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_entries_chat_id",
                table: "chat_entries",
                column: "chat_id",
                filter: "kind = 0 and not is_system_entry and not is_removed and languages is not null and languages != '' and  content != ''");
        }
    }
}
