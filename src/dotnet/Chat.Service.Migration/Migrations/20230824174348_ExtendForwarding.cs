using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class ExtendForwarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "forwarded_author_name",
                table: "chat_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "forwarded_chat_entry_begins_at",
                table: "chat_entries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "forwarded_chat_title",
                table: "chat_entries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "forwarded_author_name",
                table: "chat_entries");

            migrationBuilder.DropColumn(
                name: "forwarded_chat_entry_begins_at",
                table: "chat_entries");

            migrationBuilder.DropColumn(
                name: "forwarded_chat_title",
                table: "chat_entries");
        }
    }
}
