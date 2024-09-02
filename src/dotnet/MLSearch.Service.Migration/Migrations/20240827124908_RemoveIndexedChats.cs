using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.MLSearch.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIndexedChats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "indexed_chat");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "indexed_chat",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    chat_created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_entry_local_id = table.Column<long>(type: "bigint", nullable: false),
                    last_entry_version = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
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
    }
}
