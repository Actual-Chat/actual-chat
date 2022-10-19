using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    public partial class Reactions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_reactions",
                table: "chat_entries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "reaction_summaries",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    chat_entry_id = table.Column<string>(type: "text", nullable: false),
                    count = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    emoji = table.Column<string>(type: "text", nullable: false),
                    first_author_ids_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reaction_summaries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reactions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    author_id = table.Column<string>(type: "text", nullable: false),
                    chat_entry_id = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    emoji = table.Column<string>(type: "text", nullable: false),
                    modified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reactions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reaction_summaries_chat_entry_id",
                table: "reaction_summaries",
                column: "chat_entry_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reaction_summaries");

            migrationBuilder.DropTable(
                name: "reactions");

            migrationBuilder.DropColumn(
                name: "has_reactions",
                table: "chat_entries");
        }
    }
}
