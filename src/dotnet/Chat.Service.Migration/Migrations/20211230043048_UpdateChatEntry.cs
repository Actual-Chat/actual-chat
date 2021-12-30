using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations.Migrations
{
    public partial class UpdateChatEntry : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContentEndsAt",
                table: "ChatEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRemoved",
                table: "ChatEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Type_IsRemoved_Id",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Type", "IsRemoved", "Id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatEntries_ChatId_Type_IsRemoved_Id",
                table: "ChatEntries");

            migrationBuilder.DropColumn(
                name: "ContentEndsAt",
                table: "ChatEntries");

            migrationBuilder.DropColumn(
                name: "IsRemoved",
                table: "ChatEntries");
        }
    }
}
