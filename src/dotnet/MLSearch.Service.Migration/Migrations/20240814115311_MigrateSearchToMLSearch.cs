using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.MLSearch.Migrations
{
    /// <inheritdoc />
    public partial class MigrateSearchToMLSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_index_state",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    last_updated_id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    last_updated_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_index_state", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "indexed_chat",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    last_entry_local_id = table.Column<long>(type: "bigint", nullable: false),
                    last_entry_version = table.Column<long>(type: "bigint", nullable: false),
                    chat_created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_indexed_chat", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_indexed_chat_chat_created_at",
                table: "indexed_chat",
                column: "chat_created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_index_state");

            migrationBuilder.DropTable(
                name: "indexed_chat");
        }
    }
}
