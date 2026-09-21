using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations;

/// <inheritdoc />
public partial class _20260918002808_AddChatCleanup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "min_visible_entry_lid",
            table: "chats",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<TimeSpan>(
            name: "retention_period",
            table: "chats",
            type: "interval",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "is_purged",
            table: "chat_entries",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "removed_at",
            table: "chat_entries",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "min_visible_entry_lid",
            table: "chats");

        migrationBuilder.DropColumn(
            name: "retention_period",
            table: "chats");

        migrationBuilder.DropColumn(
            name: "is_purged",
            table: "chat_entries");

        migrationBuilder.DropColumn(
            name: "removed_at",
            table: "chat_entries");
    }
}
