using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Legacy_Renames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "user_links",
                newName: "aliases");

            migrationBuilder.RenameColumn(
                name: "emoji_id",
                table: "reactions",
                newName: "emoji");

            migrationBuilder.RenameColumn(
                name: "emoji_id",
                table: "reaction_summaries",
                newName: "emoji");

            migrationBuilder.RenameColumn(
                name: "user_link_id",
                table: "places",
                newName: "alias_id");

            migrationBuilder.RenameColumn(
                name: "user_link_id",
                table: "chats",
                newName: "alias_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "aliases",
                newName: "user_links");

            migrationBuilder.RenameColumn(
                name: "emoji",
                table: "reactions",
                newName: "emoji_id");

            migrationBuilder.RenameColumn(
                name: "emoji",
                table: "reaction_summaries",
                newName: "emoji_id");

            migrationBuilder.RenameColumn(
                name: "alias_id",
                table: "places",
                newName: "user_link_id");

            migrationBuilder.RenameColumn(
                name: "alias_id",
                table: "chats",
                newName: "user_link_id");
        }
    }
}
